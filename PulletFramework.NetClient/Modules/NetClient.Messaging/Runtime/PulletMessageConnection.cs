using System;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.Messaging;
using PulletNet.ClientSDK;
using PulletNet.Messaging;

namespace PulletFramework.NetClient.Messaging
{
    /// <summary>
    /// One explicit connection incarnation, created through the existing Runtime.
    /// The Runtime alone creates/releases the SDK client; this binding owns only its
    /// message session and requests removal of its exact source when finished.
    /// Construct/connect on the Unity thread; pump that Runtime, not another queue.
    /// </summary>
    public sealed class PulletMessageConnection : IAsyncDisposable
    {
        private readonly object _gate = new object();
        private readonly PulletClientRuntime _runtime;
        private readonly HostedClient _source;
        private readonly byte _channel;
        private readonly int _thread;
        private readonly Action<MessageInputResult> _inputDiagnostic;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _completion = Signal();
        private readonly Task<PulletClientCloseResult> _removed;
        private readonly TaskCompletionSource<bool> _observerExited = Signal();
        private Task _connectExit = Task.CompletedTask;
        private Exception _requestFailure;
        private bool _started, _observed, _ready, _closing;

        public PulletMessageConnection(PulletClientRuntime runtime, string key, NetClientOptions network,
            MessageProtocolOptions protocol, IMessageCodec codec, byte channelId = 0,
            RpcSessionOptions rpcOptions = null, NotificationRouterOptions notificationOptions = null,
            Action<NotificationFailure> diagnostic = null, Action<MessageInputResult> inputDiagnostic = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (network == null) throw new ArgumentNullException(nameof(network));
            // Never mutate raw options to make a typed binding appear compatible.
            if (network.Reconnect == null || network.Reconnect.EnableReconnect)
                throw new ArgumentException("Explicit typed connections require EnableReconnect=false.", nameof(network));
            if (network.LogicalChannels == null ? channelId != 0 :
                !network.LogicalChannels.TryGetValue(channelId, out var delivery) || delivery != ChannelType.ReliableOrdered)
                throw new ArgumentException("Select a ReliableOrdered channel (default ID 0).", nameof(channelId));
            _channel = channelId; _thread = Thread.CurrentThread.ManagedThreadId; _inputDiagnostic = inputDiagnostic;
            Session = new MessageSession(protocol, codec, SendAsync, rpcOptions, notificationOptions, diagnostic);
            try
            {
                _source = runtime.Create(key, network);
                _removed = runtime.GetRemovalCompletion(_source);
                _source.Client.ConnectionStateChanged += OnState;
                runtime.PayloadReceived += OnPayload;
            }
            catch { Session.RequestClose(); _lifetime.Dispose(); throw; }
            _ = ObserveRemovalAsync(); // completion is owned by _observerExited, including inline retirement
        }

        public string Key => _source.Key;
        /// <summary>Prepare subscriptions before connecting. Sends are rejected until NewSession is confirmed.</summary>
        public MessageSession Session { get; }
        public bool IsReady { get { lock (_gate) return _ready && !_closing && !Session.IsClosing; } }
        public bool IsClosing { get { lock (_gate) return _closing; } }
        /// <summary>Stable exit of this source, its accepted Connect and message owners. Does not close other sources.</summary>
        public Task Completion => _completion.Task;

        /// <summary>
        /// Exactly one establishment attempt. A new connection requires Close + a new binding,
        /// not resetting the registry on this source. No transparent reconnect or preserved rebind.
        /// </summary>
        public Task<ConnectionResult> ConnectAsync(CancellationToken token = default)
        {
            RequireThread();
            lock (_gate)
            {
                if (token.IsCancellationRequested) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Pre-cancelled."));
                if (_closing) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.Disposed, "Binding is closing."));
                if (_started) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "This incarnation already attempted Connect."));
                _started = true;
                var exited = Signal(); _connectExit = exited.Task;
                return ConnectCoreAsync(token, exited);
            }
        }

        private async Task<ConnectionResult> ConnectCoreAsync(CancellationToken token, TaskCompletionSource<bool> exited)
        {
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token))
                {
                    var result = await _source.Client.ConnectAsync(linked.Token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (result.IsSuccess && result.SessionContinuity == SessionContinuity.NewSession && _observed && !_closing && !Session.IsClosing)
                        { _ready = true; return result; }
                    }
                    RequestClose();
                    return result.IsSuccess ? ConnectionResult.Fail(ConnectionErrorCode.Superseded, "NewSession was not confirmed by this binding.") : result;
                }
            }
            catch { RequestClose(); throw; }
            finally { exited.TrySetResult(true); }
        }

        private async ValueTask<MessageSendResult> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken token)
        {
            ValueTask<SendResult> send;
            lock (_gate)
            {
                if (!_ready || _closing || Session.IsClosing) return MessageSendResult.Rejected;
                send = _source.Client.SendAsync(payload, _channel, token);
            }
            return (await send.ConfigureAwait(false)).IsSuccess ? MessageSendResult.Accepted : MessageSendResult.Rejected;
        }

        private void OnState(ConnectionStateChangedEvent value)
        {
            bool retire;
            lock (_gate)
            {
                if (_closing) return;
                // SDK publishes Connected before releasing new receive IO. Observe
                // that actual boundary, not the later Unity lifecycle notification.
                if (value.Current == ConnectionState.Connected && _started && !_observed)
                { _observed = true; return; }
                retire = _observed || value.Current == ConnectionState.Disconnected || value.Current == ConnectionState.Disposed;
            }
            // Subsequent recovery is intentionally unsupported: never retag its input
            // with a fresh MessageSource while the remote Session might be preserved.
            if (retire) RequestClose();
        }

        private void OnPayload(PulletClientPayloadEvent value)
        {
            lock (_gate)
                if (_closing || !_observed || !ReferenceEquals(value.Source, _source) ||
                    value.Payload.ChannelId != _channel || value.Payload.ChannelType != ChannelType.ReliableOrdered) return;
            // The exact HostedClient is used for one incarnation only. Runtime already
            // captures/checks its dispatch generation at copy/delivery; no body copy.
            var result = Session.Receive(Session.Source, value.Payload.Memory);
            // Still the existing Runtime callback: a throwing diagnostic is isolated
            // and reported through Runtime.CallbackError, without a second event queue.
            _inputDiagnostic?.Invoke(result);
        }

        /// <summary>Legal in a handler: request and return; never synchronously join your own callback.</summary>
        public void RequestClose()
        {
            lock (_gate)
            {
                if (_closing) return;
                _closing = true; _ready = false;
            }
            try { Session.RequestClose(); } catch (Exception error) { _requestFailure = error; }
            try { _lifetime.Cancel(); } catch (Exception error) { _requestFailure = Combine(_requestFailure, error); }
            _ = CloseCoreAsync(); // all failures settle the stable Completion below
        }

        private async Task CloseCoreAsync()
        {
            Exception failure = _requestFailure;
            try
            {
                try { await _runtime.RemoveAsync(_source).ConfigureAwait(false); }
                catch (ObjectDisposedException) { /* Runtime close already owns this exact removal. */ }
                catch (InvalidOperationException) when (_removed.IsCompleted) { /* Already removed, never a name lookup. */ }
                catch (Exception error) { failure = error; }
                // Stop native IO before joining message sends which may borrow it.
                var removal = await _removed.ConfigureAwait(false);
                if (!removal.IsSuccess) failure = Combine(failure, removal.CleanupError ?? new InvalidOperationException("Source delivery failed: " + removal.DeliveryFailure.Reason));
            }
            catch (Exception error) { failure = Combine(failure, error); }
            try { await _connectExit.ConfigureAwait(false); } catch (Exception error) { failure = Combine(failure, error); }
            try { await Session.Completion.ConfigureAwait(false); } catch (Exception error) { failure = Combine(failure, error); }
            // The observer only requests this close; it never waits for it.
            await _observerExited.Task.ConfigureAwait(false);
            _runtime.PayloadReceived -= OnPayload;
            _source.Client.ConnectionStateChanged -= OnState;
            _lifetime.Dispose();
            if (failure == null) _completion.TrySetResult(true); else _completion.TrySetException(failure);
        }

        private async Task ObserveRemovalAsync()
        {
            try { await _removed.ConfigureAwait(false); }
            catch { /* CloseCore reports the same authoritative removal failure. */ }
            finally
            {
                try { RequestClose(); }
                finally { _observerExited.TrySetResult(true); }
            }
        }
        public Task CloseAsync(CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            RequestClose();
            return token.CanBeCanceled ? WaitForCaller(Completion, token) : Completion;
        }
        public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Connect belongs to the constructing Unity thread.");
        }
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static Exception Combine(Exception first, Exception next) => first == null ? next : next == null ? first : new AggregateException(first, next);
        private static async Task WaitForCaller(Task shared, CancellationToken token)
        {
            var canceled = Signal();
            using (token.Register(() => canceled.TrySetCanceled(token)))
                await (await Task.WhenAny(shared, canceled.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
