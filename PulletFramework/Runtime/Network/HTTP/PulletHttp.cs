using System;
using System.Collections.Generic;

namespace PulletFramework.Network
{
    public sealed class HttpRequestHandle : IDisposable
    {
        internal long Sequence;
        internal HttpRequest Request;
        internal Action<HttpResponse> Callback;
        internal IHttpTransportOperation Operation;
        internal float NextAttemptTime;
        internal int AttemptCount;
        internal bool CancelRequested;

        public EHttpRequestState State { get; internal set; } = EHttpRequestState.Queued;
        public HttpResponse Response { get; internal set; }
        public bool IsDone => State == EHttpRequestState.Succeeded
            || State == EHttpRequestState.Failed
            || State == EHttpRequestState.Cancelled;
        public float Progress => Operation?.Progress ?? (IsDone ? 1f : 0f);

        public void Cancel()
        {
            if (IsDone || CancelRequested)
                return;
            CancelRequested = true;
            Operation?.Cancel();
        }

        public void Dispose()
        {
            Cancel();
        }
    }

    /// <summary>
    /// 有并发限制、优先级、取消和重试能力的 HTTP/HTTPS 客户端。
    /// </summary>
    public static class PulletHttp
    {
        private static readonly List<HttpRequestHandle> m_Pending = new List<HttpRequestHandle>();
        private static readonly List<HttpRequestHandle> m_Active = new List<HttpRequestHandle>();
        private static IHttpTransport m_Transport;
        private static int m_MaxConcurrentRequests = 4;
        private static float m_Time;
        private static long m_NextSequence;
        private static bool m_IsDestroying;
        private static int m_Version;

        public static int PendingCount => m_Pending.Count;
        public static int ActiveCount => m_Active.Count;

        public static void SetTransport(IHttpTransport transport)
        {
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));
            if (m_Active.Count > 0)
                throw new InvalidOperationException("Cannot replace HTTP transport while requests are active.");
            m_Transport?.Dispose();
            m_Transport = transport;
        }

        public static void SetMaxConcurrentRequests(int count)
        {
            m_MaxConcurrentRequests = Math.Max(1, count);
        }

        public static HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (m_IsDestroying)
                throw new InvalidOperationException("HTTP client is being destroyed.");
            if (string.IsNullOrEmpty(request.Url))
                throw new ArgumentException("HTTP URL cannot be empty.", nameof(request));

            EnsureTransport();
            var handle = new HttpRequestHandle
            {
                Sequence = ++m_NextSequence,
                Request = Snapshot(request),
                Callback = completed,
            };
            m_Pending.Add(handle);
            return handle;
        }

        public static HttpRequestHandle Get(string url, Action<HttpResponse> completed = null)
        {
            return Send(HttpRequest.Get(url), completed);
        }

        public static HttpRequestHandle Post(
            string url,
            byte[] body,
            string contentType = "application/octet-stream",
            Action<HttpResponse> completed = null)
        {
            return Send(HttpRequest.Post(url, body, contentType), completed);
        }

        public static HttpRequestHandle PostJson(
            string url,
            string json,
            Action<HttpResponse> completed = null)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json ?? string.Empty);
            return Post(url, body, "application/json; charset=utf-8", completed);
        }

        internal static void Update(float unscaledDeltaTime)
        {
            int version = m_Version;
            m_Time += Math.Max(0f, unscaledDeltaTime);
            UpdateActiveRequests(version);
            if (version == m_Version)
                StartPendingRequests(version);
        }

        internal static void Destroy()
        {
            m_IsDestroying = true;
            m_Version++;
            try
            {
                var active = new List<HttpRequestHandle>(m_Active);
                var pending = new List<HttpRequestHandle>(m_Pending);
                m_Active.Clear();
                m_Pending.Clear();

                for (int i = 0; i < active.Count; i++)
                {
                    active[i].Operation?.Cancel();
                    active[i].Operation?.Dispose();
                    Complete(active[i], EHttpRequestState.Cancelled, new HttpResponse { Error = "HTTP client destroyed." });
                }
                for (int i = 0; i < pending.Count; i++)
                    Complete(pending[i], EHttpRequestState.Cancelled, new HttpResponse { Error = "HTTP client destroyed." });

                m_Transport?.Dispose();
                m_Transport = null;
                m_Time = 0f;
                m_NextSequence = 0;
            }
            finally
            {
                m_IsDestroying = false;
            }
        }

        private static void UpdateActiveRequests(int version)
        {
            for (int i = m_Active.Count - 1; i >= 0; i--)
            {
                HttpRequestHandle handle = m_Active[i];
                if (handle.CancelRequested)
                {
                    handle.Operation?.Dispose();
                    handle.Operation = null;
                    m_Active.RemoveAt(i);
                    Complete(handle, EHttpRequestState.Cancelled, new HttpResponse { Error = "HTTP request cancelled." });
                    if (version != m_Version)
                        return;
                    continue;
                }
                if (handle.Operation == null || !handle.Operation.IsDone)
                    continue;

                HttpResponse response = handle.Operation.Response ?? new HttpResponse { Error = "HTTP response is null." };
                handle.Operation.Dispose();
                handle.Operation = null;
                m_Active.RemoveAt(i);

                if (ShouldRetry(handle, response))
                {
                    float multiplier = (float)Math.Pow(2d, Math.Max(0, handle.AttemptCount - 1));
                    handle.NextAttemptTime = m_Time + handle.Request.RetryDelaySeconds * multiplier;
                    handle.State = EHttpRequestState.Queued;
                    m_Pending.Add(handle);
                }
                else
                {
                    Complete(
                        handle,
                        response.IsSuccess ? EHttpRequestState.Succeeded : EHttpRequestState.Failed,
                        response);
                    if (version != m_Version)
                        return;
                }
            }
        }

        private static void StartPendingRequests(int version)
        {
            while (version == m_Version && m_Active.Count < m_MaxConcurrentRequests)
            {
                int index = FindNextPendingIndex();
                if (index < 0)
                    return;

                HttpRequestHandle handle = m_Pending[index];
                m_Pending.RemoveAt(index);
                if (handle.CancelRequested)
                {
                    Complete(handle, EHttpRequestState.Cancelled, new HttpResponse { Error = "HTTP request cancelled." });
                    if (version != m_Version)
                        return;
                    continue;
                }

                handle.AttemptCount++;
                handle.State = EHttpRequestState.Running;
                try
                {
                    handle.Operation = m_Transport.Send(handle.Request);
                    m_Active.Add(handle);
                }
                catch (Exception exception)
                {
                    var response = new HttpResponse { Error = exception.Message };
                    if (ShouldRetry(handle, response))
                    {
                        handle.NextAttemptTime = m_Time + handle.Request.RetryDelaySeconds;
                        handle.State = EHttpRequestState.Queued;
                        m_Pending.Add(handle);
                    }
                    else
                    {
                        Complete(handle, EHttpRequestState.Failed, response);
                        if (version != m_Version)
                            return;
                    }
                }
            }
        }

        private static int FindNextPendingIndex()
        {
            int bestIndex = -1;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                HttpRequestHandle candidate = m_Pending[i];
                if (candidate.NextAttemptTime > m_Time)
                    continue;
                if (bestIndex < 0
                    || candidate.Request.Priority > m_Pending[bestIndex].Request.Priority
                    || (candidate.Request.Priority == m_Pending[bestIndex].Request.Priority
                        && candidate.Sequence < m_Pending[bestIndex].Sequence))
                    bestIndex = i;
            }
            return bestIndex;
        }

        private static bool ShouldRetry(HttpRequestHandle handle, HttpResponse response)
        {
            if (handle.CancelRequested || handle.AttemptCount > handle.Request.MaxRetries)
                return false;
            long code = response.StatusCode;
            return code == 0 || code == 408 || code == 425 || code == 429 || code >= 500;
        }

        private static void Complete(HttpRequestHandle handle, EHttpRequestState state, HttpResponse response)
        {
            if (handle.IsDone)
                return;
            handle.State = state;
            handle.Response = response;
            Action<HttpResponse> callback = handle.Callback;
            handle.Callback = null;
            try
            {
                callback?.Invoke(response);
            }
            catch (Exception exception)
            {
                PLogger.Error($"[HTTP Callback Failed] {exception.Message}");
            }
        }

        private static void EnsureTransport()
        {
            PulletNetwork.EnsureInitialized();
            if (m_Transport == null)
                m_Transport = new UnityWebRequestTransport();
        }

        private static HttpRequest Snapshot(HttpRequest source)
        {
            var snapshot = new HttpRequest
            {
                Url = source.Url,
                Method = string.IsNullOrEmpty(source.Method) ? "GET" : source.Method.ToUpperInvariant(),
                Body = CopyBody(source),
                CopyBody = source.CopyBody,
                ContentType = source.ContentType,
                TimeoutSeconds = Math.Max(1, source.TimeoutSeconds),
                MaxRetries = Math.Max(0, source.MaxRetries),
                RetryDelaySeconds = Math.Max(0f, source.RetryDelaySeconds),
                Priority = source.Priority,
            };
            foreach (KeyValuePair<string, string> header in source.Headers)
                snapshot.Headers[header.Key] = header.Value;
            return snapshot;
        }

        private static byte[] CopyBody(HttpRequest source)
        {
            if (!source.CopyBody || source.Body == null)
                return source.Body;
            var copy = new byte[source.Body.Length];
            Buffer.BlockCopy(source.Body, 0, copy, 0, copy.Length);
            return copy;
        }
    }
}
