using System;
using System.Collections.Generic;
using System.Text;

namespace PulletFramework.Network
{
    public sealed class PulletWebSocketClient : IDisposable
    {
        private readonly struct OutgoingMessage
        {
            public readonly byte[] Data;
            public readonly bool IsText;

            public OutgoingMessage(byte[] data, bool isText)
            {
                Data = data;
                IsText = isText;
            }
        }

        private readonly PulletWebSocketOptions m_Options;
        private readonly IWebSocketTransportFactory m_TransportFactory;
        private readonly Queue<OutgoingMessage> m_SendQueue = new Queue<OutgoingMessage>();
        private readonly Random m_Random = new Random(Guid.NewGuid().GetHashCode());
        private IWebSocketTransport m_Transport;
        private float m_Time;
        private float m_ReconnectAt;
        private float m_HeartbeatElapsed;
        private float m_LastReceiveTime;
        private int m_ReconnectAttempts;
        private bool m_ManualClose;

        public EPulletWebSocketState State { get; private set; } = EPulletWebSocketState.Closed;
        public int QueuedMessageCount => m_SendQueue.Count;

        public event Action Connected;
        public event Action<WebSocketMessage> MessageReceived;
        public event Action<string> Error;
        public event Action<ushort, string> Disconnected;

        internal PulletWebSocketClient(
            PulletWebSocketOptions options,
            IWebSocketTransportFactory transportFactory)
        {
            m_Options = Snapshot(options ?? throw new ArgumentNullException(nameof(options)));
            m_TransportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        }

        public void Connect()
        {
            ThrowIfDisposed();
            if (State == EPulletWebSocketState.Open || State == EPulletWebSocketState.Connecting)
                return;
            if (!Uri.TryCreate(m_Options.Url, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("WebSocket URL must use ws:// or wss://.");

            m_ManualClose = false;
            StartConnection();
        }

        public bool SendText(string text)
        {
            return Send(Encoding.UTF8.GetBytes(text ?? string.Empty), true, false);
        }

        public bool SendBinary(byte[] data, bool copy = true)
        {
            return Send(data, false, copy);
        }

        public void Close(ushort code = 1000, string reason = "Normal closure")
        {
            if (State == EPulletWebSocketState.Disposed || State == EPulletWebSocketState.Closed)
                return;
            m_ManualClose = true;
            State = EPulletWebSocketState.Closing;
            try
            {
                m_Transport?.Close(code, reason);
            }
            catch (Exception exception)
            {
                RaiseError(exception.Message);
                HandleClosed(code, reason);
            }
        }

        public void Dispose()
        {
            if (State == EPulletWebSocketState.Disposed)
                return;
            m_ManualClose = true;
            m_SendQueue.Clear();
            m_Transport?.Dispose();
            m_Transport = null;
            State = EPulletWebSocketState.Disposed;
            Connected = null;
            MessageReceived = null;
            Error = null;
            Disconnected = null;
        }

        internal void Update(float unscaledDeltaTime)
        {
            if (State == EPulletWebSocketState.Disposed)
                return;
            float delta = Math.Max(0f, unscaledDeltaTime);
            m_Time += delta;

            while (m_Transport != null && m_Transport.TryDequeueEvent(out WebSocketTransportEvent socketEvent))
                ProcessEvent(socketEvent);

            if (State == EPulletWebSocketState.Reconnecting && m_Time >= m_ReconnectAt)
                StartConnection();

            if (State == EPulletWebSocketState.Open && m_Options.HeartbeatIntervalSeconds > 0f)
            {
                if (m_Options.HeartbeatTimeoutSeconds > 0f
                    && m_Time - m_LastReceiveTime >= m_Options.HeartbeatTimeoutSeconds)
                {
                    const string reason = "Heartbeat timeout";
                    RaiseError(reason);
                    try { m_Transport?.Close(4000, reason); }
                    catch (Exception exception) { RaiseError(exception.Message); }
                    HandleClosed(4000, reason);
                    return;
                }

                m_HeartbeatElapsed += delta;
                if (m_HeartbeatElapsed >= m_Options.HeartbeatIntervalSeconds)
                {
                    m_HeartbeatElapsed = 0f;
                    byte[] payload = m_Options.HeartbeatPayload;
                    if (payload != null && payload.Length > 0)
                        Send(payload, m_Options.HeartbeatIsText, false);
                }
            }
        }

        private bool Send(byte[] data, bool isText, bool copy)
        {
            ThrowIfDisposed();
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length > m_Options.MaxMessageBytes)
            {
                RaiseError($"WebSocket message exceeds {m_Options.MaxMessageBytes} bytes.");
                return false;
            }

            byte[] payload = data;
            if (copy)
            {
                payload = new byte[data.Length];
                Buffer.BlockCopy(data, 0, payload, 0, data.Length);
            }

            if (State == EPulletWebSocketState.Open)
                return SendNow(new OutgoingMessage(payload, isText));
            if (State != EPulletWebSocketState.Connecting && State != EPulletWebSocketState.Reconnecting)
                return false;
            if (m_SendQueue.Count >= m_Options.MaxQueuedMessages)
            {
                RaiseError($"WebSocket send queue reached {m_Options.MaxQueuedMessages} messages.");
                return false;
            }
            m_SendQueue.Enqueue(new OutgoingMessage(payload, isText));
            return true;
        }

        private void StartConnection()
        {
            m_Transport?.Dispose();
            m_Transport = m_TransportFactory.Create(m_Options);
            if (m_Transport == null)
                throw new InvalidOperationException("WebSocket transport factory returned null.");
            State = EPulletWebSocketState.Connecting;
            try
            {
                m_Transport.Connect(m_Options.Url, m_Options.Headers);
            }
            catch (Exception exception)
            {
                RaiseError(exception.Message);
                ScheduleReconnect();
            }
        }

        private void ProcessEvent(WebSocketTransportEvent socketEvent)
        {
            switch (socketEvent.Type)
            {
                case EWebSocketTransportEventType.Opened:
                    State = EPulletWebSocketState.Open;
                    m_ReconnectAttempts = 0;
                    m_HeartbeatElapsed = 0f;
                    m_LastReceiveTime = m_Time;
                    InvokeSafely(Connected, "Connected");
                    FlushSendQueue();
                    break;
                case EWebSocketTransportEventType.Message:
                    m_LastReceiveTime = m_Time;
                    if (socketEvent.Message.Data != null
                        && socketEvent.Message.Data.Length <= m_Options.MaxMessageBytes)
                        InvokeSafely(MessageReceived, socketEvent.Message, "MessageReceived");
                    else
                        RaiseError($"WebSocket message exceeds {m_Options.MaxMessageBytes} bytes.");
                    break;
                case EWebSocketTransportEventType.Error:
                    RaiseError(socketEvent.Detail);
                    break;
                case EWebSocketTransportEventType.Closed:
                    HandleClosed(socketEvent.CloseCode, socketEvent.Detail);
                    break;
            }
        }

        private void HandleClosed(ushort code, string reason)
        {
            if (State == EPulletWebSocketState.Disposed)
                return;
            m_Transport?.Dispose();
            m_Transport = null;
            InvokeSafely(Disconnected, code, reason, "Disconnected");
            if (m_ManualClose || !m_Options.AutoReconnect)
                State = EPulletWebSocketState.Closed;
            else
                ScheduleReconnect();
        }

        private void ScheduleReconnect()
        {
            m_Transport?.Dispose();
            m_Transport = null;
            if (m_ManualClose || !m_Options.AutoReconnect
                || (m_Options.MaxReconnectAttempts >= 0
                    && m_ReconnectAttempts >= m_Options.MaxReconnectAttempts))
            {
                State = EPulletWebSocketState.Closed;
                return;
            }

            m_ReconnectAttempts++;
            double multiplier = Math.Pow(2d, Math.Max(0, m_ReconnectAttempts - 1));
            float delay = Math.Min(
                m_Options.MaxReconnectDelaySeconds,
                m_Options.ReconnectDelaySeconds * (float)multiplier);
            float jitter = m_Options.ReconnectJitterRatio;
            if (jitter > 0f)
                delay *= 1f + ((float)m_Random.NextDouble() * 2f - 1f) * jitter;
            m_ReconnectAt = m_Time + delay;
            State = EPulletWebSocketState.Reconnecting;
        }

        private void FlushSendQueue()
        {
            while (State == EPulletWebSocketState.Open && m_SendQueue.Count > 0)
            {
                if (!SendNow(m_SendQueue.Peek()))
                    return;
                m_SendQueue.Dequeue();
            }
        }

        private bool SendNow(OutgoingMessage message)
        {
            try
            {
                m_Transport.Send(new ArraySegment<byte>(message.Data), message.IsText);
                return true;
            }
            catch (WebSocketSendQueueFullException exception)
            {
                RaiseError(exception.Message);
                return false;
            }
            catch (Exception exception)
            {
                RaiseError(exception.Message);
                ScheduleReconnect();
                return false;
            }
        }

        private void RaiseError(string error)
        {
            PLogger.Error($"[WebSocket] {error}");
            InvokeSafely(Error, error, "Error");
        }

        private static void InvokeSafely(Action callback, string eventName)
        {
            try { callback?.Invoke(); }
            catch (Exception exception) { PLogger.Error($"[WebSocket {eventName} Callback] {exception.Message}"); }
        }

        private static void InvokeSafely<T>(Action<T> callback, T value, string eventName)
        {
            try { callback?.Invoke(value); }
            catch (Exception exception) { PLogger.Error($"[WebSocket {eventName} Callback] {exception.Message}"); }
        }

        private static void InvokeSafely<T1, T2>(Action<T1, T2> callback, T1 value1, T2 value2, string eventName)
        {
            try { callback?.Invoke(value1, value2); }
            catch (Exception exception) { PLogger.Error($"[WebSocket {eventName} Callback] {exception.Message}"); }
        }

        private void ThrowIfDisposed()
        {
            if (State == EPulletWebSocketState.Disposed)
                throw new ObjectDisposedException(nameof(PulletWebSocketClient));
        }

        private static PulletWebSocketOptions Snapshot(PulletWebSocketOptions source)
        {
            var options = new PulletWebSocketOptions
            {
                Url = source.Url,
                AutoReconnect = source.AutoReconnect,
                ReconnectDelaySeconds = Math.Max(0.1f, source.ReconnectDelaySeconds),
                MaxReconnectDelaySeconds = Math.Max(0.1f, source.MaxReconnectDelaySeconds),
                ReconnectJitterRatio = Math.Min(0.5f, Math.Max(0f, source.ReconnectJitterRatio)),
                MaxReconnectAttempts = source.MaxReconnectAttempts,
                HeartbeatIntervalSeconds = Math.Max(0f, source.HeartbeatIntervalSeconds),
                HeartbeatTimeoutSeconds = Math.Max(0f, source.HeartbeatTimeoutSeconds),
                HeartbeatPayload = source.HeartbeatPayload,
                HeartbeatIsText = source.HeartbeatIsText,
                MaxQueuedMessages = Math.Max(1, source.MaxQueuedMessages),
                MaxQueuedReceiveMessages = Math.Max(1, source.MaxQueuedReceiveMessages),
                MaxMessageBytes = Math.Max(1024, source.MaxMessageBytes),
            };
            foreach (KeyValuePair<string, string> header in source.Headers)
                options.Headers[header.Key] = header.Value;
            return options;
        }
    }
}
