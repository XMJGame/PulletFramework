using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
#endif

namespace PulletFramework.Network
{
    /// <summary>
    /// 原生 Unity/Mono/IL2CPP 使用的 ClientWebSocket 传输层。
    /// WebGL 小游戏平台应注入对应 SDK 的 TransportFactory。
    /// </summary>
    public sealed class DotNetWebSocketTransportFactory : IWebSocketTransportFactory
    {
        public IWebSocketTransport Create(PulletWebSocketOptions options)
        {
            return new DotNetWebSocketTransport(
                options?.MaxMessageBytes ?? 1024 * 1024,
                options?.MaxQueuedMessages ?? 128,
                options?.MaxQueuedReceiveMessages ?? 256);
        }
    }

    public sealed class DotNetWebSocketTransport : IWebSocketTransport
    {
        private readonly int m_MaxMessageBytes;
        private readonly int m_MaxQueuedMessages;
        private readonly int m_MaxQueuedReceiveMessages;
        private int m_QueuedEventCount;
        private readonly ConcurrentQueue<WebSocketTransportEvent> m_Events =
            new ConcurrentQueue<WebSocketTransportEvent>();

#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly struct OutgoingMessage
        {
            public readonly ArraySegment<byte> Data;
            public readonly WebSocketMessageType Type;

            public OutgoingMessage(ArraySegment<byte> data, WebSocketMessageType type)
            {
                Data = data;
                Type = type;
            }
        }

        private readonly ConcurrentQueue<OutgoingMessage> m_SendQueue =
            new ConcurrentQueue<OutgoingMessage>();
        private ClientWebSocket m_Socket;
        private CancellationTokenSource m_Cancellation;
        private int m_IsSending;
        private int m_QueuedSendCount;
        private int m_ClosedEventRaised;
#endif

        public DotNetWebSocketTransport(
            int maxMessageBytes = 1024 * 1024,
            int maxQueuedMessages = 128,
            int maxQueuedReceiveMessages = 256)
        {
            m_MaxMessageBytes = Math.Max(1024, maxMessageBytes);
            m_MaxQueuedMessages = Math.Max(1, maxQueuedMessages);
            m_MaxQueuedReceiveMessages = Math.Max(1, maxQueuedReceiveMessages);
        }

        public void Connect(string url, IReadOnlyDictionary<string, string> headers)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            DisposeSocket();
            m_ClosedEventRaised = 0;
            m_Cancellation = new CancellationTokenSource();
            m_Socket = new ClientWebSocket();
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                    m_Socket.Options.SetRequestHeader(header.Key, header.Value);
            }
            _ = ConnectAsync(new Uri(url), m_Cancellation.Token);
#else
            EnqueueControl(WebSocketTransportEvent.Error(
                "ClientWebSocket is unavailable on WebGL. Register a mini-game IWebSocketTransportFactory."));
            EnqueueControl(WebSocketTransportEvent.Closed(1006, "Unsupported platform"));
#endif
        }

        public void Send(ArraySegment<byte> data, bool isText)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (m_Socket == null || m_Socket.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not open.");
            if (Interlocked.Increment(ref m_QueuedSendCount) > m_MaxQueuedMessages)
            {
                Interlocked.Decrement(ref m_QueuedSendCount);
                throw new WebSocketSendQueueFullException(
                    $"WebSocket send queue reached {m_MaxQueuedMessages} messages.");
            }
            m_SendQueue.Enqueue(new OutgoingMessage(
                data,
                isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary));
            if (Interlocked.CompareExchange(ref m_IsSending, 1, 0) == 0)
                _ = SendQueuedAsync();
#else
            throw new PlatformNotSupportedException("ClientWebSocket is unavailable on WebGL.");
#endif
        }

        public void Close(ushort code, string reason)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ = CloseAsync(code, reason);
#else
            RaiseClosed(code, reason);
#endif
        }

        public bool TryDequeueEvent(out WebSocketTransportEvent socketEvent)
        {
            if (!m_Events.TryDequeue(out socketEvent))
                return false;
            Interlocked.Decrement(ref m_QueuedEventCount);
            return true;
        }

        public void Dispose()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            DisposeSocket();
#endif
            while (m_Events.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref m_QueuedEventCount, 0);
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {
                await m_Socket.ConnectAsync(uri, cancellationToken);
                EnqueueControl(WebSocketTransportEvent.Opened());
                await ReceiveLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RaiseClosed(1000, "Cancelled");
            }
            catch (Exception exception)
            {
                EnqueueControl(WebSocketTransportEvent.Error(exception.Message));
                RaiseClosed(1006, exception.Message);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            while (m_Socket != null && m_Socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await m_Socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    ushort code = result.CloseStatus.HasValue ? (ushort)result.CloseStatus.Value : (ushort)1000;
                    RaiseClosed(code, result.CloseStatusDescription);
                    return;
                }

                bool isText = result.MessageType == WebSocketMessageType.Text;
                if (result.EndOfMessage)
                {
                    if (result.Count > m_MaxMessageBytes)
                        throw new InvalidDataException($"WebSocket message exceeds {m_MaxMessageBytes} bytes.");
                    var data = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, data, 0, result.Count);
                    EnqueueMessage(data, isText);
                    continue;
                }

                using (var stream = new MemoryStream(Math.Min(m_MaxMessageBytes, result.Count + buffer.Length)))
                {
                    if (result.Count > m_MaxMessageBytes)
                        throw new InvalidDataException($"WebSocket message exceeds {m_MaxMessageBytes} bytes.");
                    stream.Write(buffer, 0, result.Count);
                    do
                    {
                        result = await m_Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (stream.Length + result.Count > m_MaxMessageBytes)
                            throw new InvalidDataException($"WebSocket message exceeds {m_MaxMessageBytes} bytes.");
                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);
                    EnqueueMessage(stream.ToArray(), isText);
                }
            }
            RaiseClosed(1006, "Connection ended.");
        }

        private async Task SendQueuedAsync()
        {
            try
            {
                while (m_SendQueue.TryDequeue(out OutgoingMessage message))
                {
                    Interlocked.Decrement(ref m_QueuedSendCount);
                    if (m_Socket == null || m_Socket.State != WebSocketState.Open)
                        throw new InvalidOperationException("WebSocket disconnected while sending.");
                    await m_Socket.SendAsync(message.Data, message.Type, true, m_Cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                EnqueueControl(WebSocketTransportEvent.Error(exception.Message));
                RaiseClosed(1006, exception.Message);
            }
            finally
            {
                Interlocked.Exchange(ref m_IsSending, 0);
                if (!m_SendQueue.IsEmpty && Interlocked.CompareExchange(ref m_IsSending, 1, 0) == 0)
                    _ = SendQueuedAsync();
            }
        }

        private async Task CloseAsync(ushort code, string reason)
        {
            try
            {
                if (m_Socket != null && m_Socket.State == WebSocketState.Open)
                {
                    await m_Socket.CloseAsync(
                        (WebSocketCloseStatus)code,
                        reason ?? string.Empty,
                        CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                EnqueueControl(WebSocketTransportEvent.Error(exception.Message));
            }
            finally
            {
                RaiseClosed(code, reason);
                m_Cancellation?.Cancel();
            }
        }

        private void DisposeSocket()
        {
            m_Cancellation?.Cancel();
            m_Cancellation?.Dispose();
            m_Cancellation = null;
            m_Socket?.Dispose();
            m_Socket = null;
            while (m_SendQueue.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref m_IsSending, 0);
            Interlocked.Exchange(ref m_QueuedSendCount, 0);
        }
#endif

        private void RaiseClosed(ushort code, string reason)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (Interlocked.Exchange(ref m_ClosedEventRaised, 1) != 0)
                return;
#endif
            EnqueueControl(WebSocketTransportEvent.Closed(code, reason));
        }

        private void EnqueueMessage(byte[] data, bool isText)
        {
            if (Interlocked.Increment(ref m_QueuedEventCount) > m_MaxQueuedReceiveMessages)
            {
                Interlocked.Decrement(ref m_QueuedEventCount);
                EnqueueControl(WebSocketTransportEvent.Error(
                    $"WebSocket receive queue reached {m_MaxQueuedReceiveMessages} messages."));
#if !UNITY_WEBGL || UNITY_EDITOR
                m_Cancellation?.Cancel();
#endif
                RaiseClosed(1009, "Receive queue overflow");
                return;
            }
            m_Events.Enqueue(WebSocketTransportEvent.Received(data, isText));
        }

        private void EnqueueControl(WebSocketTransportEvent socketEvent)
        {
            Interlocked.Increment(ref m_QueuedEventCount);
            m_Events.Enqueue(socketEvent);
        }
    }
}
