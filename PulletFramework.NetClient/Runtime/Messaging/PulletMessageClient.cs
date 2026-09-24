using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;

namespace PulletFramework.Messaging
{
    /// <summary>位于传输 SDK 与具体业务之间的通用强类型消息客户端。</summary>
    public sealed class PulletMessageClient : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Func<byte[], ChannelType, CancellationToken, ValueTask<SendResult>> _sendAsync;
        private readonly PulletMessageProtocolOptions _protocol;
        private readonly IMessageSerializer _serializer;
        private readonly PulletMessageRouter _router;
        private readonly Dictionary<ulong, IPendingCall> _pending = new Dictionary<ulong, IPendingCall>();
        private long _nextCorrelationId;
        private bool _disposed;

        /// <summary>消息信封合法，但当前没有任何模块订阅该 MessageId。</summary>
        public event Action<uint> UnhandledMessage;
        /// <summary>消息帧、序列化或 RPC 对应关系无效。</summary>
        public event Action<string> ProtocolError;

        public PulletMessageClient(
            PulletMessageProtocolOptions protocol,
            IMessageSerializer serializer,
            Func<byte[], ChannelType, CancellationToken, ValueTask<SendResult>> sendAsync)
        {
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            _router = new PulletMessageRouter(serializer);
        }

        /// <summary>订阅强类型消息；调用方负责释放返回的订阅句柄。</summary>
        public IMessageSubscription Subscribe<T>(uint messageId, Action<T> handler)
            => _router.Subscribe(messageId, handler);

        /// <summary>显式取消订阅；也可以直接调用订阅句柄的 Unsubscribe 或 Dispose。</summary>
        public bool Unsubscribe(IMessageSubscription subscription)
        {
            if (subscription == null || !subscription.IsSubscribed) return false;
            subscription.Unsubscribe();
            return true;
        }

        /// <summary>发送一条不需要响应的强类型通知。</summary>
        public async Task SendAsync<T>(
            uint messageId,
            T value,
            ChannelType channel = ChannelType.ReliableOrdered,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            byte[] payload = Encode(PulletMessageKind.Notification, messageId, 0, value);
            SendResult result = await _sendAsync(payload, channel, cancellationToken);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.Message ?? result.ErrorCode.ToString());
        }

        /// <summary>发送请求并等待对应响应，支持超时、外部取消和断线取消。</summary>
        public async Task<TResponse> CallAsync<TRequest, TResponse>(
            uint requestId,
            uint responseId,
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            ulong correlationId = NextCorrelationId();
            var completion = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                ThrowIfDisposed();
                _pending.Add(correlationId, new PendingCall<TResponse>(responseId, completion, _serializer));
            }

            using (var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // 发送和等待响应共用同一个截止时间，避免发送阻塞后重新开始计时。
                Task deadline = Task.Delay(timeout, deadlineCts.Token);
                Task<SendResult> send = null;
                try
                {
                    byte[] payload = Encode(PulletMessageKind.Request, requestId, correlationId, request);
                    send = _sendAsync(payload, ChannelType.ReliableOrdered, deadlineCts.Token).AsTask();
                    Task first = await Task.WhenAny(send, deadline, completion.Task);
                    if (first == deadline)
                        ThrowDeadlineOrCancellation(requestId, responseId, correlationId, cancellationToken);
                    if (first == completion.Task && !send.IsCompleted)
                    {
                        // 断线取消 RPC 时不能被仍在等待的发送阻塞。
                        if (completion.Task.IsFaulted || completion.Task.IsCanceled)
                            return await completion.Task;
                        first = await Task.WhenAny(send, deadline);
                        if (first == deadline)
                            ThrowDeadlineOrCancellation(requestId, responseId, correlationId, cancellationToken);
                    }

                    SendResult result = await send;
                    if (!result.IsSuccess)
                        throw new InvalidOperationException(result.Message ?? result.ErrorCode.ToString());

                    Task finished = await Task.WhenAny(completion.Task, deadline);
                    if (finished == deadline)
                        ThrowDeadlineOrCancellation(requestId, responseId, correlationId, cancellationToken);
                    return await completion.Task;
                }
                finally
                {
                    deadlineCts.Cancel();
                    // 自定义发送器可能不响应取消；若调用已结束，仍须观察其迟到异常。
                    if (send != null)
                        _ = send.ContinueWith(task => { var ignored = task.Exception; },
                            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    lock (_gate) _pending.Remove(correlationId);
                }
            }
        }

        /// <summary>解码并分发一个完整应用 Payload；应在 Unity 主线程调用。</summary>
        public bool HandlePayload(byte[] payload, out string error)
        {
            error = string.Empty;
            if (_disposed) { error = "Message client is disposed."; return false; }
            if (!PulletMessageFrame.TryDecode(_protocol, payload, out PulletMessageFrame frame, out error))
            {
                ProtocolError?.Invoke(error);
                return false;
            }

            if (frame.Kind == PulletMessageKind.Response)
            {
                if (!TryComplete(frame, out error))
                {
                    error = $"Unexpected RPC response: MessageId={frame.MessageId}, CorrelationId={frame.CorrelationId}.";
                    ProtocolError?.Invoke(error);
                    return false;
                }
                if (!string.IsNullOrEmpty(error)) ProtocolError?.Invoke(error);
                return string.IsNullOrEmpty(error);
            }

            bool handled = _router.Dispatch(frame, out error);
            if (!handled && string.IsNullOrEmpty(error)) UnhandledMessage?.Invoke(frame.MessageId);
            else if (!string.IsNullOrEmpty(error)) ProtocolError?.Invoke(error);
            return handled;
        }

        /// <summary>连接断开时调用，立即结束所有属于旧会话的 RPC。</summary>
        public void CancelPending(string reason)
        {
            IPendingCall[] calls;
            lock (_gate)
            {
                calls = new IPendingCall[_pending.Count];
                _pending.Values.CopyTo(calls, 0);
                _pending.Clear();
            }
            var exception = new OperationCanceledException(reason ?? "Messaging session ended.");
            for (int i = 0; i < calls.Length; i++) calls[i].Fail(exception);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            CancelPending("Messaging client was disposed.");
            _router.Dispose();
            UnhandledMessage = null;
            ProtocolError = null;
        }

        private byte[] Encode<T>(PulletMessageKind kind, uint messageId, ulong correlationId, T value)
        {
            return PulletMessageFrame.Encode(
                _protocol, _serializer.Encoding, kind, messageId, correlationId, _serializer.Serialize(value));
        }

        private bool TryComplete(PulletMessageFrame frame, out string error)
        {
            error = string.Empty;
            IPendingCall pending;
            lock (_gate)
            {
                if (!_pending.TryGetValue(frame.CorrelationId, out pending)) return false;
            }
            if (pending.ResponseMessageId != frame.MessageId)
            {
                error = $"RPC response mismatch: expected {pending.ResponseMessageId}, received {frame.MessageId}.";
                pending.Fail(new InvalidOperationException(error));
                return true;
            }
            try { pending.Complete(frame.Body); }
            catch (Exception exception)
            {
                error = "RPC response deserialization failed: " + exception.Message;
                pending.Fail(exception);
            }
            return true;
        }

        private ulong NextCorrelationId()
        {
            long next = Interlocked.Increment(ref _nextCorrelationId);
            if (next <= 0)
            {
                Interlocked.Exchange(ref _nextCorrelationId, 1);
                next = 1;
            }
            return (ulong)next;
        }

        private static void ThrowDeadlineOrCancellation(
            uint requestId, uint responseId, ulong correlationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"RPC {requestId}->{responseId} timed out. CorrelationId={correlationId}.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PulletMessageClient));
        }

        private interface IPendingCall
        {
            uint ResponseMessageId { get; }
            void Complete(byte[] body);
            void Fail(Exception exception);
        }

        private sealed class PendingCall<T> : IPendingCall
        {
            private readonly TaskCompletionSource<T> _completion;
            private readonly IMessageSerializer _serializer;
            public uint ResponseMessageId { get; }

            public PendingCall(uint responseMessageId, TaskCompletionSource<T> completion,
                IMessageSerializer serializer)
            {
                ResponseMessageId = responseMessageId;
                _completion = completion;
                _serializer = serializer;
            }

            public void Complete(byte[] body) => _completion.TrySetResult(_serializer.Deserialize<T>(body));
            public void Fail(Exception exception) => _completion.TrySetException(exception);
        }
    }
}
