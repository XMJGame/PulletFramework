using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.Discovery;

namespace PulletFramework.Discovery
{
    /// <summary>
    /// 拥有一轮有界发现及Unity交付；构造、Discover和Update在Unity主线程执行。
    /// 原生Discovery负责IO、编码与限额；关闭等待IO和已进入的回调退出，不依赖后续Update。
    /// <para>One bounded discovery round and its Unity handoff. Construct/Discover/Update on
    /// the Unity thread. Native Discovery owns IO/codec/limits; close joins IO and entered
    /// callbacks without requiring further Update calls.</para>
    /// </summary>
    public sealed class DiscoveryRuntime : IAsyncDisposable
    {
        private const int MaximumNotices = 34; // start + at most 32 native attempts + terminal
        private readonly object _gate = new object();
        private readonly Queue<Notice> _notices = new Queue<Notice>();
        private readonly Func<DiscoveryOptions, Action<int, int>, CancellationToken, Task<DiscoveryResult>> _discover;
        private readonly int _thread, _perUpdate;
        private Round _round;
        private long _nextId, _callbackErrors;
        private bool _closing;
        private int _updating, _callbacks;
        private TaskCompletionSource<bool> _callbacksExited;
        private Task _close;

        public event Action<long> Started;
        public event Action<long, int, int> Attempted;
        public event Action<DiscoveryCompletion> Completed;
        public event Action<Exception> CallbackError;
        public long CallbackFailures => Interlocked.Read(ref _callbackErrors);
        public bool IsDiscovering { get { lock (_gate) return _round != null && !_round.Completion.Task.IsCompleted; } }
        public int PendingNotices { get { lock (_gate) return _notices.Count; } }

        public DiscoveryRuntime(int callbacksPerUpdate = 16)
            : this(new DiscoveryClient().DiscoverAsync, callbacksPerUpdate) { }
        internal DiscoveryRuntime(Func<DiscoveryOptions, Action<int, int>, CancellationToken, Task<DiscoveryResult>> discover,
            int callbacksPerUpdate = 16)
        {
            if (callbacksPerUpdate < 1 || callbacksPerUpdate > MaximumNotices) throw new ArgumentOutOfRangeException(nameof(callbacksPerUpdate));
            _discover = discover ?? throw new ArgumentNullException(nameof(discover));
            _perUpdate = callbacksPerUpdate; _thread = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>IO completion does not imply Unity notification delivery. Pump terminal delivery before starting another round.</summary>
        public Task<DiscoverySnapshot> DiscoverAsync(DiscoveryOptions options, CancellationToken token = default)
        {
            RequireThread();
            if (options == null) throw new ArgumentNullException(nameof(options));
            token.ThrowIfCancellationRequested();
            Round round;
            lock (_gate)
            {
                if (_closing) throw new ObjectDisposedException(nameof(DiscoveryRuntime));
                if (_round != null) throw new InvalidOperationException("A round or its terminal notification is still owned. Pump Update or Close.");
                round = new Round(checked(++_nextId), token);
                _round = round;
                _notices.Enqueue(new Notice(round, 0));
            }
            // Completion was published before invoking native code, including its synchronous validation/progress.
            _ = ExecuteAsync(round, options);
            return round.Completion.Task;
        }

        private async Task ExecuteAsync(Round round, DiscoveryOptions options)
        {
            DiscoverySnapshot result = null; Exception failure = null; bool canceled = false;
            try
            {
                var native = await _discover(options, (attempt, maximum) =>
                {
                    lock (_gate)
                    {
                        if (_closing || !ReferenceEquals(_round, round)) return;
                        if (attempt < 1 || attempt > 32 || maximum > 32 || _notices.Count >= MaximumNotices - 1)
                            throw new InvalidOperationException("Native discovery progress exceeded its bounded contract.");
                        _notices.Enqueue(new Notice(round, 1, attempt, maximum));
                    }
                }, round.Token).ConfigureAwait(false);
                result = new DiscoverySnapshot(round.Id, native);
            }
            catch (OperationCanceledException error) when (round.Token.IsCancellationRequested) { failure = error; canceled = true; }
            catch (Exception error) { failure = error; }
            // Native task already includes physical send/receive exit. Dispose linked registration before publication.
            round.Cancellation.Dispose();
            lock (_gate)
            {
                var completion = new DiscoveryCompletion(round.Id, result, failure, canceled);
                if (!_closing) _notices.Enqueue(new Notice(round, 2, completion: completion));
                if (canceled) round.Completion.TrySetCanceled(round.Token);
                else if (failure != null) round.Completion.TrySetException(failure);
                else round.Completion.TrySetResult(result);
            }
        }

        /// <summary>Request cancellation and join native IO. Does not silently discard pending UI facts or reopen admission.</summary>
        public Task StopAsync()
        {
            Round round; lock (_gate) round = _round;
            return StopRoundAsync(round);
        }

        public Task CloseAsync()
        {
            TaskCompletionSource<bool> owner; Round round; Task callbacks;
            lock (_gate)
            {
                if (_close != null) return _close;
                _closing = true; _notices.Clear(); round = _round;
                callbacks = _callbacks == 0 ? Task.CompletedTask : _callbacksExited.Task;
                owner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = owner.Task;
            }
            _ = CloseCoreAsync(round, callbacks, owner);
            return _close;
        }

        private static async Task StopRoundAsync(Round round)
        {
            if (round == null) return;
            Exception cancellationError = null;
            try { round.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { } // native operation may already have completed/disposed
            catch (Exception error) { cancellationError = error; }
            try { await round.Completion.Task.ConfigureAwait(false); }
            catch (OperationCanceledException) when (round.Token.IsCancellationRequested) { }
            catch (Exception error) { if (cancellationError != null) throw new AggregateException(cancellationError, error); throw; }
            if (cancellationError != null) throw cancellationError;
        }

        private async Task CloseCoreAsync(Round round, Task callbacks, TaskCompletionSource<bool> owner)
        {
            Exception failure = null;
            try { await StopRoundAsync(round).ConfigureAwait(false); } catch (Exception error) { failure = error; }
            try { await callbacks.ConfigureAwait(false); } catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            lock (_gate) _round = null;
            if (failure == null) owner.TrySetResult(true); else owner.TrySetException(failure);
        }

        public void Update()
        {
            RequireThread();
            if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0) throw new InvalidOperationException("Recursive Update is not supported.");
            try
            {
                for (int i = 0; i < _perUpdate; i++)
                {
                    Notice notice;
                    lock (_gate)
                    {
                        if (_closing || _notices.Count == 0) return;
                        notice = _notices.Dequeue(); _callbacks++;
                        if (_callbacks == 1) _callbacksExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    try
                    {
                        if (notice.Kind == 0) Invoke(Started, handler => ((Action<long>)handler)(notice.Round.Id));
                        else if (notice.Kind == 1) Invoke(Attempted, handler => ((Action<long, int, int>)handler)(notice.Round.Id, notice.Attempt, notice.Maximum));
                        else Invoke(Completed, handler => ((Action<DiscoveryCompletion>)handler)(notice.Completion));
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (notice.Kind == 2 && ReferenceEquals(_round, notice.Round)) _round = null;
                            if (--_callbacks == 0) _callbacksExited.TrySetResult(true);
                        }
                    }
                }
            }
            finally { Volatile.Write(ref _updating, 0); }
        }

        private void Invoke(Delegate handlers, Action<Delegate> invoke)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                lock (_gate) if (_closing) return;
                try { invoke(handler); }
                catch (Exception error)
                {
                    Interlocked.Increment(ref _callbackErrors);
                    var diagnostics = CallbackError;
                    if (diagnostics == null) continue;
                    foreach (Action<Exception> report in diagnostics.GetInvocationList())
                        try { report(error); } catch { Interlocked.Increment(ref _callbackErrors); }
                }
            }
        }
        private void RequireThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Discover/Update belongs to the constructing Unity thread."); }
        public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

        private sealed class Round
        {
            internal readonly long Id;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken Token;
            internal readonly TaskCompletionSource<DiscoverySnapshot> Completion = new TaskCompletionSource<DiscoverySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Round(long id, CancellationToken token)
            { Id = id; Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); Token = Cancellation.Token; }
        }
        private sealed class Notice
        {
            internal readonly Round Round; internal readonly int Kind, Attempt, Maximum;
            internal readonly DiscoveryCompletion Completion;
            internal Notice(Round round, int kind, int attempt = 0, int maximum = 0, DiscoveryCompletion completion = null)
            { Round = round; Kind = kind; Attempt = attempt; Maximum = maximum; Completion = completion; }
        }
    }
}
