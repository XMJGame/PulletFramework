using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PulletFramework.Network
{
    /// <summary>
    /// Unity 原生 HTTP/HTTPS 传输层。小游戏平台可替换为平台 SDK Provider。
    /// </summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        private sealed class Operation : IHttpTransportOperation
        {
            private UnityWebRequest m_Request;
            private UnityWebRequestAsyncOperation m_Operation;
            private HttpResponse m_Response;

            public bool IsDone => m_Operation == null || m_Operation.isDone;
            public float Progress => m_Operation == null ? 1f : m_Operation.progress;

            public HttpResponse Response
            {
                get
                {
                    if (m_Response == null && IsDone)
                        m_Response = BuildResponse();
                    return m_Response;
                }
            }

            public Operation(HttpRequest request)
            {
                m_Request = new UnityWebRequest(request.Url, request.Method ?? "GET");
                m_Request.downloadHandler = new DownloadHandlerBuffer();
                if (request.Body != null && request.Body.Length > 0)
                    m_Request.uploadHandler = new UploadHandlerRaw(request.Body);
                if (!string.IsNullOrEmpty(request.ContentType))
                    m_Request.SetRequestHeader("Content-Type", request.ContentType);
                foreach (KeyValuePair<string, string> header in request.Headers)
                    m_Request.SetRequestHeader(header.Key, header.Value);
                m_Request.timeout = Mathf.Max(1, request.TimeoutSeconds);
                m_Operation = m_Request.SendWebRequest();
            }

            public void Cancel()
            {
                m_Request?.Abort();
            }

            public void Dispose()
            {
                m_Request?.Dispose();
                m_Request = null;
                m_Operation = null;
            }

            private HttpResponse BuildResponse()
            {
                if (m_Request == null)
                    return new HttpResponse { Error = "HTTP transport was disposed." };

                string error = m_Request.result == UnityWebRequest.Result.Success ? null : m_Request.error;
                return new HttpResponse
                {
                    StatusCode = m_Request.responseCode,
                    Data = m_Request.downloadHandler?.data,
                    Error = error,
                    Headers = m_Request.GetResponseHeaders() ?? new Dictionary<string, string>(),
                };
            }
        }

        public IHttpTransportOperation Send(HttpRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.Url))
                throw new ArgumentException("HTTP URL cannot be empty.", nameof(request));
            return new Operation(request);
        }

        public void Dispose()
        {
        }
    }
}
