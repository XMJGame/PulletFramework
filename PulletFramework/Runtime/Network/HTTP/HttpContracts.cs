using System;
using System.Collections.Generic;

namespace PulletFramework.Network
{
    public enum EHttpRequestState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled,
    }

    public sealed class HttpRequest
    {
        public string Url { get; set; }
        public string Method { get; set; } = "GET";
        public byte[] Body { get; set; }
        public bool CopyBody { get; set; } = true;
        public string ContentType { get; set; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        public int TimeoutSeconds { get; set; } = 15;
        public int MaxRetries { get; set; }
        public float RetryDelaySeconds { get; set; } = 0.5f;
        public int Priority { get; set; }

        public static HttpRequest Get(string url)
        {
            return new HttpRequest { Url = url, Method = "GET", MaxRetries = 2 };
        }

        public static HttpRequest Post(string url, byte[] body, string contentType = "application/octet-stream")
        {
            return new HttpRequest
            {
                Url = url,
                Method = "POST",
                Body = body,
                ContentType = contentType,
            };
        }
    }

    public sealed class HttpResponse
    {
        public long StatusCode { get; internal set; }
        public byte[] Data { get; internal set; }
        public string Error { get; internal set; }
        public Dictionary<string, string> Headers { get; internal set; }
        public bool IsSuccess => string.IsNullOrEmpty(Error) && StatusCode >= 200 && StatusCode < 300;
        public string Text => Data == null ? string.Empty : System.Text.Encoding.UTF8.GetString(Data);
    }

    public interface IHttpTransportOperation : IDisposable
    {
        bool IsDone { get; }
        float Progress { get; }
        HttpResponse Response { get; }
        void Cancel();
    }

    public interface IHttpTransport : IDisposable
    {
        IHttpTransportOperation Send(HttpRequest request);
    }
}
