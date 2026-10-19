using System;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.Messaging;

namespace PulletFramework.Messaging
{
    /// <summary>
    /// 单个来源代际的消息会话，不是网络客户端。应用绑定准确的可靠有序发送方/来源，
    /// 在创建它的Unity线程提交借用Payload；保留原网络会话时不可重建registry，否则会重置RPC编号。
    /// <para>One message incarnation, not a network client. The application binds its exact
    /// reliable-ordered sender/source and feeds borrowed payloads on the creating Unity thread.
    /// Never replace this registry on a preserved wire session; it would reset RPC IDs.</para>
    /// </summary>
    public sealed class MessageSession : IAsyncDisposable
    {
        private readonly MessageProtocolOptions _protocol;
        private readonly RpcSession _rpc;
        private readonly NotificationRouter _notifications;
        private readonly int _thread;
        private int _closing;

        public MessageSession(MessageProtocolOptions protocol, IMessageCodec codec, MessageSender sender,
            RpcSessionOptions rpcOptions = null, NotificationRouterOptions notificationOptions = null,
            Action<NotificationFailure> diagnostic = null)
        {
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _thread = Thread.CurrentThread.ManagedThreadId;
            _rpc = new RpcSession(protocol, codec, sender, rpcOptions);
            try { _notifications = new NotificationRouter(protocol, codec, _rpc.Source, notificationOptions, diagnostic); }
            catch { _rpc.RequestClose(); throw; } // no native resource or admitted work exists during construction
            Completion = Task.WhenAll(_rpc.Completion, _notifications.Completion);
        }

        public MessageSource Source => _rpc.Source;
        public RpcSessionSnapshot RpcSnapshot => _rpc.Snapshot;
        public NotificationRouterSnapshot NotificationSnapshot => _notifications.Snapshot;
        public bool IsClosing => Volatile.Read(ref _closing) != 0;
        /// <summary>Stable real exit of message work only, not network/IO disposal or caller-owned inboxes.</summary>
        public Task Completion { get; }

        public Task<TResponse> CallAsync<TRequest, TResponse>(uint requestId, uint responseId, TRequest value,
            TimeSpan timeout, CancellationToken token = default)
        {
            RequireOpenThread();
            return _rpc.CallAsync<TRequest, TResponse>(requestId, responseId, value, timeout, token);
        }
        public Task<MessageSendResult> SendNotificationAsync<T>(uint id, T value, TimeSpan timeout,
            CancellationToken token = default)
        {
            RequireOpenThread();
            return _rpc.SendNotificationAsync(id, value, timeout, token);
        }
        public Task<MessageSendResult> SendResponseAsync<T>(uint id, ulong rpcId, T value, TimeSpan timeout,
            CancellationToken token = default)
        {
            RequireOpenThread();
            return _rpc.SendResponseAsync(id, rpcId, value, timeout, token);
        }
        public IMessageSubscription Subscribe<T>(uint id, Action<T> handler)
        {
            RequireOpenThread();
            return _notifications.Subscribe(id, handler);
        }

        /// <summary>
        /// Synchronous, borrowed input. Route each frame to exactly one existing engine.
        /// No body copy or extra payload queue. Source is supplied by the binding, not inferred from bytes.
        /// </summary>
        public MessageInputResult Receive(MessageSource source, ReadOnlyMemory<byte> payload)
        {
            RequireThread();
            if (!ReferenceEquals(source, Source)) return new MessageInputResult(MessageInputKind.SourceMismatch);
            if (IsClosing) return new MessageInputResult(MessageInputKind.Closed);
            if (payload.Length < MessageFrame.HeaderSize)
                return new MessageInputResult(MessageInputKind.InvalidFrame, FrameError.Truncated);
            // The selected engine validates the full frame. Reading the discriminator
            // is not admission: malformed packets still cannot enter a codec/callback.
            switch ((MessageKind)payload.Span[4])
            {
                case MessageKind.Response:
                    var response = _rpc.ReceiveResponse(source, payload);
                    return new MessageInputResult(MessageInputKind.Response, ErrorIfInvalid(response == ResponseResult.InvalidFrame, payload), response);
                case MessageKind.Notification:
                    var report = _notifications.Dispatch(source, payload);
                    return new MessageInputResult(MessageInputKind.Notification, report.FrameError, notification: report);
                default:
                    bool valid = MessageFrameCodec.TryDecode(_protocol, payload, out _, out var error);
                    return new MessageInputResult(valid ? MessageInputKind.UnexpectedKind : MessageInputKind.InvalidFrame, error);
            }
        }
        private FrameError ErrorIfInvalid(bool invalid, ReadOnlyMemory<byte> payload)
        {
            if (!invalid) return FrameError.None;
            MessageFrameCodec.TryDecode(_protocol, payload, out _, out var error);
            return error;
        }

        /// <summary>Legal inside codecs/handlers. Closes both admissions, but does not claim completion.</summary>
        public void RequestClose()
        {
            if (Interlocked.Exchange(ref _closing, 1) != 0) return;
            try { _rpc.RequestClose(); }
            finally { _notifications.RequestClose(); }
        }
        /// <summary>
        /// One shared exit task. A canceled waiter cannot cancel shared cleanup.
        /// From an entered callback, request/return; never synchronously wait or await yourself.
        /// </summary>
        public Task CloseAsync(CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            RequestClose();
            return token.CanBeCanceled ? WaitForCaller(Completion, token) : Completion;
        }
        public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
        private void RequireOpenThread()
        {
            RequireThread();
            if (IsClosing) throw new ObjectDisposedException(nameof(MessageSession));
        }
        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("Message API and receive dispatch belong to the constructing Unity thread.");
        }
        private static async Task WaitForCaller(Task shared, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetCanceled(token)))
                await (await Task.WhenAny(shared, canceled.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
