using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PulletFramework.NetClient
{
    [Serializable]
    public sealed class PulletServerInfo
    {
        [JsonProperty("magic")] public string magic;
        [JsonProperty("version")] public int version;
        [JsonProperty("requestId")] public string requestId;
        [JsonProperty("instanceId")] public string instanceId;
        [JsonProperty("serverId")] public string serverId;
        [JsonProperty("serverName")] public string serverName;
        [JsonProperty("serviceType")] public string serviceType;
        [JsonProperty("host")] public string host;
        [JsonProperty("tcpPort")] public int tcpPort;
        [JsonProperty("udpPort")] public int udpPort;
        [JsonProperty("webPort")] public int webPort;
        [JsonProperty("status")] public string status;
        [JsonProperty("protocolVersion")] public string protocolVersion;

        public override string ToString()
            => $"{serverName ?? serverId ?? "PulletNet Server"} ({host}:{tcpPort}, {status ?? "Unknown"})";
    }

    internal sealed class DiscoveryRequest
    {
        [JsonProperty("magic")] public string magic;
        [JsonProperty("version")] public int version;
        [JsonProperty("requestId")] public string requestId;
        [JsonProperty("serviceType")] public string serviceType;
    }

    /// <summary>
    /// PulletNet 局域网服务器发现入口。一次发现会按固定间隔发送有限次广播，
    /// 在时间窗口内收集全部匹配服务器；既可 await，也可使用 Start/Stop 和事件。
    /// </summary>
    public static class PulletServerDiscovery
    {
        public const string Magic = "PULLET_DISCOVERY";

        private static readonly object Gate = new object();
        private static CancellationTokenSource _activeCts;
        private static int _isDiscovering;

        public static bool IsDiscovering => Volatile.Read(ref _isDiscovering) != 0;
        public static int AttemptCount { get; private set; }
        public static int MaximumAttempts { get; private set; }

        public static event Action OnStarted;
        public static event Action<int, int> OnAttempt;
        public static event Action<IReadOnlyList<PulletServerInfo>> OnSuccess;
        public static event Action<string> OnFailure;
        public static event Action OnStopped;

        /// <summary>开始一次有次数上限的发现。已有发现运行时返回 false。</summary>
        public static bool StartDiscovery(
            int port,
            float timeoutSeconds = 3f,
            int maxAttempts = 5,
            float sendIntervalSeconds = 0.5f,
            string serviceType = "pulletnet")
        {
            CancellationTokenSource cts;
            lock (Gate)
            {
                if (_activeCts != null) return false;
                ValidateArguments(port, timeoutSeconds, maxAttempts, sendIntervalSeconds);
                cts = new CancellationTokenSource();
                _activeCts = cts;
                AttemptCount = 0;
                MaximumAttempts = maxAttempts;
                Volatile.Write(ref _isDiscovering, 1);
            }

            PublishSafely(OnStarted);
            _ = RunPublishedDiscoveryAsync(
                cts, port, timeoutSeconds, maxAttempts, sendIntervalSeconds, serviceType);
            return true;
        }

        /// <summary>停止当前发现。停止是正常取消，只触发 OnStopped，不触发 OnFailure。</summary>
        public static void StopDiscovery()
        {
            CancellationTokenSource cts;
            lock (Gate) cts = _activeCts;
            cts?.Cancel();
        }

        /// <summary>兼容简洁 await 调用；默认最多发送 5 次广播。</summary>
        public static Task<IReadOnlyList<PulletServerInfo>> DiscoverAsync(
            int port,
            float timeoutSeconds = 3f,
            string serviceType = "pulletnet",
            CancellationToken cancellationToken = default)
            => DiscoverAsync(port, timeoutSeconds, 5, 0.5f, serviceType, null, cancellationToken);

        /// <summary>执行一次发现并返回整个时间窗口内发现的服务器列表。</summary>
        public static async Task<IReadOnlyList<PulletServerInfo>> DiscoverAsync(
            int port,
            float timeoutSeconds,
            int maxAttempts,
            float sendIntervalSeconds,
            string serviceType,
            Action<int, int> onAttempt,
            CancellationToken cancellationToken = default)
        {
            ValidateArguments(port, timeoutSeconds, maxAttempts, sendIntervalSeconds);

            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var interval = TimeSpan.FromSeconds(sendIntervalSeconds);
            var requestId = Guid.NewGuid().ToString("N");
            var request = new DiscoveryRequest
            {
                magic = Magic,
                version = 1,
                requestId = requestId,
                serviceType = serviceType ?? string.Empty
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));
            var found = new Dictionary<string, PulletServerInfo>(StringComparer.OrdinalIgnoreCase);

            using (var client = new UdpClient(0) { EnableBroadcast = true, MulticastLoopback = false })
            using (cancellationToken.Register(client.Close))
            {
                var destination = new IPEndPoint(IPAddress.Broadcast, port);
                var stopwatch = Stopwatch.StartNew();
                var nextSendAt = TimeSpan.Zero;
                int attempt = 0;
                Task<UdpReceiveResult> pendingReceive = null;

                while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
                {
                    if (attempt < maxAttempts && stopwatch.Elapsed >= nextSendAt)
                    {
                        await client.SendAsync(bytes, bytes.Length, destination);
                        attempt++;
                        PublishSafely(onAttempt, attempt, maxAttempts);
                        nextSendAt = stopwatch.Elapsed + interval;
                    }

                    TimeSpan remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero) break;
                    try
                    {
                        if (pendingReceive == null)
                            pendingReceive = client.ReceiveAsync();

                        TimeSpan slice = remaining < TimeSpan.FromMilliseconds(100)
                            ? remaining
                            : TimeSpan.FromMilliseconds(100);
                        Task completed = await Task.WhenAny(
                            pendingReceive,
                            Task.Delay(slice, cancellationToken));
                        if (completed != pendingReceive) continue;

                        UdpReceiveResult packet = pendingReceive.Result;
                        pendingReceive = null;
                        PulletServerInfo info;
                        try
                        {
                            info = JsonConvert.DeserializeObject<PulletServerInfo>(
                                Encoding.UTF8.GetString(packet.Buffer));
                        }
                        catch (JsonException)
                        {
                            continue;
                        }

                        if (info == null || info.magic != Magic || info.version != 1 || info.requestId != requestId)
                            continue;
                        if (!string.IsNullOrEmpty(serviceType) &&
                            !string.Equals(info.serviceType, serviceType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (IsUnusableAdvertisedHost(info.host))
                            info.host = packet.RemoteEndPoint.Address.ToString();
                        string key = string.IsNullOrWhiteSpace(info.instanceId)
                            ? $"{info.serverId}@{info.host}"
                            : info.instanceId;
                        found[key] = info;
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new List<PulletServerInfo>(found.Values);
        }

        private static async Task RunPublishedDiscoveryAsync(
            CancellationTokenSource owner,
            int port,
            float timeoutSeconds,
            int maxAttempts,
            float sendIntervalSeconds,
            string serviceType)
        {
            try
            {
                IReadOnlyList<PulletServerInfo> servers = await DiscoverAsync(
                    port,
                    timeoutSeconds,
                    maxAttempts,
                    sendIntervalSeconds,
                    serviceType,
                    (attempt, maximum) =>
                    {
                        AttemptCount = attempt;
                        PublishSafely(OnAttempt, attempt, maximum);
                    },
                    owner.Token);

                if (servers.Count > 0)
                    PublishSafely(OnSuccess, servers);
                else
                    PublishSafely(OnFailure, $"No matching server responded after {maxAttempts} discovery attempts.");
            }
            catch (OperationCanceledException) when (owner.IsCancellationRequested)
            {
                // Manual stop is a normal completion path.
            }
            catch (Exception ex)
            {
                PublishSafely(OnFailure, ex.Message);
            }
            finally
            {
                bool ownsSession;
                lock (Gate)
                {
                    ownsSession = ReferenceEquals(_activeCts, owner);
                    if (ownsSession)
                    {
                        _activeCts = null;
                        Volatile.Write(ref _isDiscovering, 0);
                    }
                }

                owner.Dispose();
                if (ownsSession) PublishSafely(OnStopped);
            }
        }

        private static void PublishSafely(Action handlers)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        private static void PublishSafely<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        private static void PublishSafely<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second)
        {
            if (handlers == null) return;
            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
            {
                try { handler(first, second); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        private static void ValidateArguments(
            int port,
            float timeoutSeconds,
            int maxAttempts,
            float sendIntervalSeconds)
        {
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (timeoutSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            if (sendIntervalSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(sendIntervalSeconds));
        }

        private static bool IsUnusableAdvertisedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!IPAddress.TryParse(host, out IPAddress address))
                return false;
            return IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address) || IPAddress.IsLoopback(address);
        }
    }
}
