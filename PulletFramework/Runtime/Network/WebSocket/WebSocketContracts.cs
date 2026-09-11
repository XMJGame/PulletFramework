using System;
using System.Collections.Generic;

namespace PulletFramework.Network
{
    public sealed class WebSocketSendQueueFullException : InvalidOperationException
    {
        public WebSocketSendQueueFullException(string message) : base(message)
        {
        }
    }

    public enum EPulletWebSocketState
    {
        Closed,
        Connecting,
        Open,
        Closing,
        Reconnecting,
        Disposed,
    }

    public enum EWebSocketTransportEventType
    {
        Opened,
        Message,
        Error,
        Closed,
    }

    public readonly struct WebSocketMessage
    {
        public byte[] Data { get; }
        public bool IsText { get; }

        public WebSocketMessage(byte[] data, bool isText)
        {
            Data = data;
            IsText = isText;
        }

        public string Text => Data == null ? string.Empty : System.Text.Encoding.UTF8.GetString(Data);
    }

    public readonly struct WebSocketTransportEvent
    {
        public EWebSocketTransportEventType Type { get; }
        public WebSocketMessage Message { get; }
        public ushort CloseCode { get; }
        public string Detail { get; }

        private WebSocketTransportEvent(
            EWebSocketTransportEventType type,
            WebSocketMessage message,
            ushort closeCode,
            string detail)
        {
            Type = type;
            Message = message;
            CloseCode = closeCode;
            Detail = detail;
        }

        public static WebSocketTransportEvent Opened()
            => new WebSocketTransportEvent(EWebSocketTransportEventType.Opened, default(WebSocketMessage), 0, null);

        public static WebSocketTransportEvent Received(byte[] data, bool isText)
            => new WebSocketTransportEvent(
                EWebSocketTransportEventType.Message,
                new WebSocketMessage(data, isText),
                0,
                null);

        public static WebSocketTransportEvent Error(string error)
            => new WebSocketTransportEvent(EWebSocketTransportEventType.Error, default(WebSocketMessage), 0, error);

        public static WebSocketTransportEvent Closed(ushort code, string reason)
            => new WebSocketTransportEvent(EWebSocketTransportEventType.Closed, default(WebSocketMessage), code, reason);
    }

    public interface IWebSocketTransport : IDisposable
    {
        void Connect(string url, IReadOnlyDictionary<string, string> headers);
        void Send(ArraySegment<byte> data, bool isText);
        void Close(ushort code, string reason);
        bool TryDequeueEvent(out WebSocketTransportEvent socketEvent);
    }

    public interface IWebSocketTransportFactory
    {
        IWebSocketTransport Create(PulletWebSocketOptions options);
    }

    public sealed class PulletWebSocketOptions
    {
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        public bool AutoReconnect { get; set; } = true;
        public float ReconnectDelaySeconds { get; set; } = 1f;
        public float MaxReconnectDelaySeconds { get; set; } = 15f;
        public float ReconnectJitterRatio { get; set; } = 0.2f;
        public int MaxReconnectAttempts { get; set; } = -1;
        public float HeartbeatIntervalSeconds { get; set; }
        public float HeartbeatTimeoutSeconds { get; set; }
        public byte[] HeartbeatPayload { get; set; }
        public bool HeartbeatIsText { get; set; }
        public int MaxQueuedMessages { get; set; } = 128;
        public int MaxQueuedReceiveMessages { get; set; } = 256;
        public int MaxMessageBytes { get; set; } = 1024 * 1024;
    }
}
