using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    /// <summary>
    /// Thin Unity delivery adapter. Construct on the Unity main thread and call
    /// Update there. SDK NetClientHost alone owns client creation/final release.
    /// No automatic connect, discovery, session simulation or extra payload queue.
    /// </summary>
    public sealed class PulletClientRuntime : IAsyncDisposable
    {
        private readonly object _gate = new object();
        private readonly NetClientHost _host;
        private readonly ClientDispatchQueue _payloads;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly int _thread, _eventLimit, _eventsPerFrame, _maximum;
        private readonly Action<HostedClient> _beforeRemove;
        private readonly Subscribers<PulletClientPayloadEvent> _payloadHandlers = new Subscribers<PulletClientPayloadEvent>();
        private readonly Subscribers<PulletClientLifecycleEvent> _lifecycleHandlers = new Subscribers<PulletClientLifecycleEvent>();
        private readonly Subscribers<PulletClientDeliveryFailure> _failureHandlers = new Subscribers<PulletClientDeliveryFailure>();
        private readonly Subscribers<PulletClientCallbackError> _errorHandlers = new Subscribers<PulletClientCallbackError>();
        private Task<PulletClientRuntimeCloseResult> _close;
        private bool _closing;
        private int _updating, _nextEvent, _pendingEvents;
        private long _diagnosticErrors;

        public event Action<PulletClientPayloadEvent> PayloadReceived { add => _payloadHandlers.Add(value); remove => _payloadHandlers.Remove(value); }
        public event Action<PulletClientLifecycleEvent> LifecycleChanged { add => _lifecycleHandlers.Add(value); remove => _lifecycleHandlers.Remove(value); }
        public event Action<PulletClientDeliveryFailure> DeliveryFaulted { add => _failureHandlers.Add(value); remove => _failureHandlers.Remove(value); }
        public event Action<PulletClientCallbackError> CallbackError { add => _errorHandlers.Add(value); remove => _errorHandlers.Remove(value); }

        public PulletClientRuntime(PulletClientRuntimeOptions options = null) : this(options, null) { }
        // A bounded cleanup seam, never a production task runner or external-client adoption API.
        internal PulletClientRuntime(PulletClientRuntimeOptions options, Action<HostedClient> beforeRemove)
        {
            options = options ?? new PulletClientRuntimeOptions();
            var limits = options.FreezePayloads();
            _host = new NetClientHost(limits.MaximumSources);
            _payloads = new ClientDispatchQueue(limits);
            _maximum = limits.MaximumSources; _eventLimit = options.LifecycleEventsPerClient;
            _eventsPerFrame = options.LifecycleCallbacksPerFrame; _thread = Thread.CurrentThread.ManagedThreadId;
            _beforeRemove = beforeRemove;
            _host.PayloadReceived += OnPayload;
        }

        /// <summary>Create without connecting. Client is borrowed: do not Dispose it or remove it through another owner.</summary>
        public HostedClient Create(string key, NetClientOptions options)
        {
            lock (_gate)
            {
                RequireOpen();
                if (key == null) throw new ArgumentNullException(nameof(key));
                if (_byKey.ContainsKey(key)) throw new InvalidOperationException("Name is still reserved by a source or its terminal delivery.");
                if (_entries.Count >= _maximum) throw new InvalidOperationException("Runtime source capacity reached.");
                var source = _host.Create(key, options);
                var entry = new Entry(source, _payloads.Register(source));
                _entries.Add(entry); _byKey.Add(key, entry);
                entry.State = value => OnState(source, value);
                entry.Connected = value => Record(source, PulletClientLifecycleKind.Connected, value);
                entry.Disconnected = value => Record(source, PulletClientLifecycleKind.Disconnected, value);
                entry.ConnectFailed = value => Record(source, PulletClientLifecycleKind.ConnectFailed, value);
                entry.Attempt = value => Record(source, PulletClientLifecycleKind.ReconnectAttempt, value);
                entry.Reconnected = () => Record(source, PulletClientLifecycleKind.Reconnected, null);
                entry.ReconnectFailed = value => Record(source, PulletClientLifecycleKind.ReconnectFailed, value);
                entry.CallbackError = value => RecordError(source, value);
                source.Client.ConnectionStateChanged += entry.State;
                source.Client.Connected += entry.Connected; source.Client.Disconnected += entry.Disconnected;
                source.Client.ConnectFailed += entry.ConnectFailed; source.Client.ReconnectAttempt += entry.Attempt;
                source.Client.Reconnected += entry.Reconnected; source.Client.ReconnectFailed += entry.ReconnectFailed;
                source.Client.CallbackError += entry.CallbackError;
                return source;
            }
        }

        public bool TryGetClient(string key, out HostedClient source)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            lock (_gate)
            {
                source = !_closing && _byKey.TryGetValue(key, out var entry) && entry.Active ? entry.Source : null;
                return source != null;
            }
        }

        /// <summary>
        /// Capture an exact registration's stable removal result before relinquishing it.
        /// Observation does not request removal. The captured task remains valid after
        /// name reuse or Runtime close; retain it rather than looking up the name later.
        /// </summary>
        public Task<PulletClientCloseResult> GetRemovalCompletion(HostedClient source)
        { lock (_gate) return RequireEntry(source).Removed.Task; }

        /// <summary>Request removal of an exact handle. Never synchronously wait in a callback; cancellation abandons only this wait.</summary>
        public Task<PulletClientCloseResult> RemoveAsync(HostedClient source, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled<PulletClientCloseResult>(ct);
            lock (_gate)
            {
                RequireOpen();
                var entry = RequireEntry(source);
                BeginRemove(entry);
                return WaitForCaller(entry.Removed.Task, ct);
            }
        }

        /// <summary>Irreversible shared close, joining SDK owners AND all already-entered Unity callbacks. Does not need Update to progress.</summary>
        public Task<PulletClientRuntimeCloseResult> CloseAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled<PulletClientRuntimeCloseResult>(ct);
            lock (_gate)
            {
                if (_close == null)
                {
                    _closing = true;
                    var owner = new TaskCompletionSource<PulletClientRuntimeCloseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _close = owner.Task;
                    var tasks = new Task<PulletClientCloseResult>[_entries.Count];
                    var callbacks = new Task[_entries.Count];
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        BeginRemove(_entries[i]); tasks[i] = _entries[i].Removed.Task;
                        callbacks[i] = _entries[i].CallbacksExited.Task;
                    }
                    var payloadExit = _payloads.CloseAsync();
                    Schedule(() => CloseCoreAsync(tasks, callbacks, payloadExit, owner), error => owner.TrySetException(error));
                }
                return WaitForCaller(_close, ct);
            }
        }

        public PulletClientRuntimeSnapshot GetSnapshot()
        { lock (_gate) return new PulletClientRuntimeSnapshot(_entries.Count, _payloads.Snapshot(), _pendingEvents, Interlocked.Read(ref _diagnosticErrors)); }

        /// <summary>One main-thread frame. Lifecycle/failure records have bounded fair delivery before each source's payloads.</summary>
        public void Update()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Update belongs to the constructing Unity thread.");
            if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0) throw new InvalidOperationException("Recursive Update is not supported.");
            try
            {
                for (int i = 0; i < _eventsPerFrame; i++)
                {
                    Entry entry = null; Control record = null; PulletClientDeliveryFailure failure = null;
                    lock (_gate)
                    {
                        if (_closing) return;
                        for (int seen = 0; seen < _entries.Count; seen++)
                        {
                            var candidate = _entries[_nextEvent]; _nextEvent = (_nextEvent + 1) % _entries.Count;
                            if (candidate.Failure != null && !candidate.FailureReported)
                            { entry = candidate; failure = candidate.Failure; candidate.FailureReported = true; break; }
                            if (candidate.Active && candidate.Events.Count != 0)
                            { entry = candidate; record = candidate.Events.Dequeue(); break; }
                        }
                        if (entry != null) EnterCallback(entry);
                    }
                    if (entry == null) break;
                    try
                    {
                        if (failure != null) Invoke(_failureHandlers.Snapshot, failure, entry, false);
                        else if (record.Error != null) ReportCallbackError(entry.Source, record.Error);
                        else Invoke(_lifecycleHandlers.Snapshot, record.Event, entry, true);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (record != null) { entry.PendingEvents--; _pendingEvents--; }
                            entry.Callbacks--; CallbackExited(entry);
                            if (entry.Active && entry.Events.Count == 0) _payloads.SetDeliveryReady(entry.Payloads, true);
                            TryForget(entry);
                        }
                    }
                }
                _payloads.Drain(DeliverPayload, ReportCallbackError);
            }
            finally { Volatile.Write(ref _updating, 0); }
        }

        // All SDK facts are observed synchronously, never reconstructed from a
        // possibly stale bool carried through the main-thread queue.
        internal void OnState(HostedClient source, ConnectionStateChangedEvent value)
        {
            lock (_gate)
            {
                if (!TryActive(source, out var entry)) return;
                switch (value.Current)
                {
                    case ConnectionState.Handshaking:
                    case ConnectionState.Disconnecting:
                    case ConnectionState.Disconnected:
                    case ConnectionState.Suspended:
                    case ConnectionState.Disposed:
                        _payloads.Invalidate(entry.Payloads); entry.NewSession = true; break;
                    case ConnectionState.Reconnecting: _payloads.Pause(entry.Payloads); break;
                    case ConnectionState.Connected:
                        if (entry.NewSession) _payloads.BeginSession(entry.Payloads); else _payloads.Resume(entry.Payloads);
                        entry.NewSession = false; break;
                }
                EnqueueControl(entry, new Control(new PulletClientLifecycleEvent(source,
                    _payloads.ReadGeneration(entry.Payloads), PulletClientLifecycleKind.StateChanged, value), null));
            }
        }

        internal void Record(HostedClient source, PulletClientLifecycleKind kind, object detail)
        {
            lock (_gate)
                if (TryActive(source, out var entry)) EnqueueControl(entry, new Control(new PulletClientLifecycleEvent(source,
                    _payloads.ReadGeneration(entry.Payloads), kind, detail), null));
        }

        // A previous application subscriber can invalidate the session during
        // this borrowed delivery; later subscribers must recheck its generation.
        internal bool IsCurrentPayload(HostedClient source, long generation)
        {
            lock (_gate) return TryActive(source, out var entry) &&
                _payloads.ReadGeneration(entry.Payloads) == generation;
        }

        private void RecordError(HostedClient source, Exception error)
        {
            lock (_gate) if (TryActive(source, out var entry)) EnqueueControl(entry, new Control(null, error));
        }

        private void EnqueueControl(Entry entry, Control record)
        {
            if (entry.PendingEvents >= _eventLimit) { Fail(entry, PulletClientDeliveryFailureReason.LifecycleOverflow, null); return; }
            entry.Events.Enqueue(record); entry.PendingEvents++; _pendingEvents++;
            _payloads.SetDeliveryReady(entry.Payloads, false);
        }

        private void OnPayload(HostedPayloadEvent value) => Receive(value.Source, value.Payload);
        internal void Receive(HostedClient source, ReceivedPayload payload)
        {
            Entry entry;
            lock (_gate) if (!TryActive(source, out entry)) return;
            _payloads.Enqueue(entry.Payloads, payload);
            lock (_gate)
            {
                if (!entry.Active) return;
                var failure = _payloads.Failure(entry.Payloads, out var error);
                if (failure != ClientDispatchFailure.None) Fail(entry,
                    failure == ClientDispatchFailure.ReliableOverflow ? PulletClientDeliveryFailureReason.ReliableOverflow : PulletClientDeliveryFailureReason.CopyFailure, error);
            }
        }

        private void Fail(Entry entry, PulletClientDeliveryFailureReason reason, Exception error)
        {
            if (entry.Failure == null) entry.Failure = new PulletClientDeliveryFailure(entry.Source, reason, error, entry.Removed.Task);
            BeginRemove(entry);
        }

        private void BeginRemove(Entry entry)
        {
            if (entry.Removing) return;
            entry.Removing = true; entry.Active = false;
            _pendingEvents -= entry.Events.Count; entry.PendingEvents -= entry.Events.Count; entry.Events.Clear();
            var payloadExit = _payloads.RetireAsync(entry.Payloads);
            CallbackExited(entry);
            Schedule(() => RemoveCoreAsync(entry, payloadExit), error => entry.Removed.TrySetException(error));
        }

        private async Task RemoveCoreAsync(Entry entry, Task payloadExit)
        {
            Exception failure = null;
            try { _beforeRemove?.Invoke(entry.Source); } catch (Exception error) { failure = error; }
            try
            {
                var result = await _host.RemoveAsync(entry.Source).ConfigureAwait(false);
                failure = Combine(failure, result.Error);
            }
            catch (Exception error) { failure = Combine(failure, error); }
            try { await payloadExit.ConfigureAwait(false); } catch (Exception error) { failure = Combine(failure, error); }
            try { Unbind(entry); } catch (Exception error) { failure = Combine(failure, error); }
            while (true)
            {
                Task callbacks;
                lock (_gate)
                {
                    // A terminal notification may enter after an earlier idle
                    // signal completed. Publish removal against current callback
                    // ownership in the same lock, never an obsolete idle task.
                    if (entry.Callbacks == 0)
                    {
                        entry.Result = new PulletClientCloseResult(entry.Source, entry.Failure, failure);
                        TryForget(entry);
                        entry.Removed.TrySetResult(entry.Result);
                        return;
                    }
                    callbacks = entry.CallbacksExited.Task;
                }
                await callbacks.ConfigureAwait(false);
            }
        }

        private async Task CloseCoreAsync(Task<PulletClientCloseResult>[] tasks, Task[] callbacks, Task payloadExit,
            TaskCompletionSource<PulletClientRuntimeCloseResult> owner)
        {
            try
            {
                Exception error = null;
                PulletClientCloseResult[] results = null;
                try { results = await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception ex) { error = ex; }
                // A terminal notification can enter after its source's SDK
                // removal already completed. Closing freezes future entry and
                // must join this current callback, not a previously idle signal.
                try { await Task.WhenAll(callbacks).ConfigureAwait(false); }
                catch (Exception ex) { error = Combine(error, ex); }
                // Even an unexpected adapter error must still attempt the SDK
                // host owner. No individual removal can skip that fallback join.
                try
                {
                    var host = await _host.CloseAllAsync().ConfigureAwait(false);
                    foreach (var client in host.Clients) error = Combine(error, client.Error);
                }
                catch (Exception ex) { error = Combine(error, ex); }
                try { await payloadExit.ConfigureAwait(false); } catch (Exception ex) { error = Combine(error, ex); }
                if (results == null)
                {
                    lock (_gate)
                    {
                        results = new PulletClientCloseResult[tasks.Length];
                        for (int i = 0; i < tasks.Length; i++) results[i] = tasks[i].Status == TaskStatus.RanToCompletion
                            ? tasks[i].Result : new PulletClientCloseResult(_entries[i].Source, _entries[i].Failure, tasks[i].Exception ?? error);
                    }
                }
                _host.PayloadReceived -= OnPayload;
                lock (_gate) { _entries.Clear(); _byKey.Clear(); }
                owner.TrySetResult(new PulletClientRuntimeCloseResult(results, error));
            }
            catch (Exception error) { owner.TrySetException(error); }
        }

        private void DeliverPayload(HostedClient source, long generation, ReceivedPayload payload)
        {
            Entry entry;
            lock (_gate)
            {
                if (!TryActive(source, out entry) || _payloads.ReadGeneration(entry.Payloads) != generation) return;
                EnterCallback(entry);
            }
            try
            {
                var value = new PulletClientPayloadEvent(source, generation, payload);
                foreach (var handler in _payloadHandlers.Snapshot)
                {
                    lock (_gate) if (!entry.Active || _closing || _payloads.ReadGeneration(entry.Payloads) != generation) break;
                    try { handler(value); } catch (Exception error) { ReportCallbackError(source, error); }
                }
            }
            finally { lock (_gate) { entry.Callbacks--; CallbackExited(entry); } }
        }

        private void Invoke<T>(Action<T>[] handlers, T value, Entry entry, bool requireActive)
        {
            foreach (var handler in handlers)
            {
                lock (_gate) if (_closing || requireActive && !entry.Active) break;
                try { handler(value); } catch (Exception error) { ReportCallbackError(entry.Source, error); }
            }
        }

        private void ReportCallbackError(HostedClient source, Exception error)
        {
            var value = new PulletClientCallbackError(source, error);
            foreach (var handler in _errorHandlers.Snapshot)
                try { handler(value); } catch { Interlocked.Increment(ref _diagnosticErrors); }
        }

        private void CallbackExited(Entry entry)
        { if (!entry.Active && entry.Callbacks == 0) entry.CallbacksExited.TrySetResult(true); }

        private static void EnterCallback(Entry entry)
        {
            if (entry.Callbacks == 0 && entry.CallbacksExited.Task.IsCompleted)
                entry.CallbacksExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            entry.Callbacks++;
        }

        private void TryForget(Entry entry)
        {
            if (_closing || entry.Result == null || entry.Result.CleanupError != null || entry.Callbacks != 0 ||
                entry.Failure != null && !entry.FailureReported) return;
            int index = _entries.IndexOf(entry); if (index < 0) return;
            _entries.RemoveAt(index); _byKey.Remove(entry.Source.Key);
            if (index < _nextEvent) _nextEvent--;
            if (_nextEvent >= _entries.Count) _nextEvent = 0;
        }

        private bool TryActive(HostedClient source, out Entry entry)
        {
            entry = null;
            return !_closing && source != null && _byKey.TryGetValue(source.Key, out entry) &&
                ReferenceEquals(entry.Source, source) && entry.Active;
        }
        private Entry RequireEntry(HostedClient source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!_byKey.TryGetValue(source.Key, out var entry) || !ReferenceEquals(entry.Source, source))
                throw new InvalidOperationException("Source is not a current runtime registration.");
            return entry;
        }
        private void RequireOpen() { if (_closing) throw new ObjectDisposedException(nameof(PulletClientRuntime)); }
        private static Exception Combine(Exception first, Exception next) => first == null ? next : next == null ? first : new AggregateException(first, next);

        // Bounded by registrations, one per removal plus one shared close task.
        // Suppress the reporting SDK callback's ownership markers before cleanup.
        private static void Schedule(Func<Task> work, Action<Exception> failed)
        {
            async Task Run()
            {
                try { await work().ConfigureAwait(false); }
                catch (Exception error) { failed(error); }
            }
            if (ExecutionContext.IsFlowSuppressed()) _ = Task.Run(Run);
            else using (ExecutionContext.SuppressFlow()) _ = Task.Run(Run);
        }
        private static Task<T> WaitForCaller<T>(Task<T> task, CancellationToken ct)
            => ct.CanBeCanceled ? WaitAsync(task, ct) : task;
        private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct)
        {
            if (task.IsCompleted) return await task.ConfigureAwait(false);
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task) ct.ThrowIfCancellationRequested();
                return await task.ConfigureAwait(false);
            }
        }
        public async ValueTask DisposeAsync()
        {
            var result = await CloseAsync().ConfigureAwait(false);
            if (result.IsSuccess) return;
            throw new InvalidOperationException("Runtime closed with delivery or cleanup failures; inspect CloseAsync results.", result.HostCleanupError);
        }

        private static void Unbind(Entry entry)
        {
            var client = entry.Source.Client;
            client.ConnectionStateChanged -= entry.State; client.Connected -= entry.Connected; client.Disconnected -= entry.Disconnected;
            client.ConnectFailed -= entry.ConnectFailed; client.ReconnectAttempt -= entry.Attempt; client.Reconnected -= entry.Reconnected;
            client.ReconnectFailed -= entry.ReconnectFailed; client.CallbackError -= entry.CallbackError;
        }
        private sealed class Entry
        {
            internal readonly HostedClient Source;
            internal readonly ClientDispatchQueue.Source Payloads;
            internal readonly Queue<Control> Events = new Queue<Control>();
            internal readonly TaskCompletionSource<PulletClientCloseResult> Removed = new TaskCompletionSource<PulletClientCloseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<bool> CallbacksExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Active = true, NewSession = true, Removing, FailureReported;
            internal int PendingEvents, Callbacks;
            internal PulletClientDeliveryFailure Failure;
            internal PulletClientCloseResult Result;
            internal Action<ConnectionStateChangedEvent> State;
            internal Action<ConnectedEvent> Connected;
            internal Action<DisconnectedEvent> Disconnected;
            internal Action<ConnectFailedEvent> ConnectFailed;
            internal Action<ReconnectAttemptEvent> Attempt;
            internal Action Reconnected;
            internal Action<ReconnectFailedEvent> ReconnectFailed;
            internal Action<Exception> CallbackError;
            internal Entry(HostedClient source, ClientDispatchQueue.Source payloads) { Source = source; Payloads = payloads; }
        }
        private sealed class Control
        {
            internal readonly PulletClientLifecycleEvent Event;
            internal readonly Exception Error;
            internal Control(PulletClientLifecycleEvent value, Exception error) { Event = value; Error = error; }
        }
        // Copy on subscription, not once per payload callback.
        internal sealed class Subscribers<T>
        {
            private readonly object _gate = new object();
            private Action<T> _handlers;
            private Action<T>[] _snapshot = Array.Empty<Action<T>>();
            internal Action<T>[] Snapshot => Volatile.Read(ref _snapshot);
            internal void Add(Action<T> handler)
            {
                if (handler == null) return;
                lock (_gate)
                {
                    _handlers += handler; Publish();
                }
            }
            internal void Remove(Action<T> handler)
            {
                lock (_gate)
                {
                    _handlers -= handler; Publish();
                }
            }
            private void Publish()
            {
                var delegates = _handlers?.GetInvocationList() ?? Array.Empty<Delegate>();
                var copy = new Action<T>[delegates.Length];
                for (int i = 0; i < copy.Length; i++) copy[i] = (Action<T>)delegates[i];
                Volatile.Write(ref _snapshot, copy);
            }
        }
    }
}
