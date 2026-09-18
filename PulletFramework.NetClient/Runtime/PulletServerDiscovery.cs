using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PulletFramework.NetClient
{
    [Serializable]
    public sealed class PulletServerInfo
    {
        public string magic;
        public int version;
        public string requestId;
        public string instanceId;
        public string serverId;
        public string serverName;
        public string serviceType;
        public string host;
        public int tcpPort;
        public int udpPort;
        public int webPort;
        public string status;
        public string protocolVersion;
    }

    internal sealed class DiscoveryRequest
    {
        public string magic;
        public int version;
        public string requestId;
        public string serviceType;
    }

    public static class PulletServerDiscovery
    {
        public const string Magic = "PULLET_DISCOVERY";

        public static async Task<IReadOnlyList<PulletServerInfo>> DiscoverAsync(
            int port,
            float timeoutSeconds = 2f,
            string serviceType = "pulletnet",
            CancellationToken cancellationToken = default)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));

            var timeout = TimeSpan.FromSeconds(Math.Max(0.25f, timeoutSeconds));
            var requestId = Guid.NewGuid().ToString("N");
            var request = new DiscoveryRequest
            {
                magic = Magic,
                version = 1,
                requestId = requestId,
                serviceType = serviceType ?? string.Empty
            };
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            var found = new Dictionary<string, PulletServerInfo>(StringComparer.OrdinalIgnoreCase);

            using (var client = new UdpClient(0) { EnableBroadcast = true, MulticastLoopback = false })
            using (cancellationToken.Register(client.Close))
            {
                var destination = new IPEndPoint(IPAddress.Broadcast, port);
                var stopwatch = Stopwatch.StartNew();
                var nextSendAt = TimeSpan.Zero;
                Task<UdpReceiveResult> pendingReceive = null;

                while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
                {
                    if (stopwatch.Elapsed >= nextSendAt)
                    {
                        await client.SendAsync(bytes, bytes.Length, destination);
                        nextSendAt = stopwatch.Elapsed + TimeSpan.FromMilliseconds(500);
                    }

                    var remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    try
                    {
                        if (pendingReceive == null)
                            pendingReceive = client.ReceiveAsync();

                        var completed = await Task.WhenAny(pendingReceive, Task.Delay(
                            remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200),
                            cancellationToken));
                        if (completed != pendingReceive)
                            continue;

                        var packet = pendingReceive.Result;
                        pendingReceive = null;
                        PulletServerInfo info;
                        try
                        {
                            info = JsonUtility.FromJson<PulletServerInfo>(Encoding.UTF8.GetString(packet.Buffer));
                        }
                        catch (ArgumentException)
                        {
                            continue;
                        }
                        if (info == null || info.magic != Magic || info.version != 1 || info.requestId != requestId)
                            continue;
                        if (!string.IsNullOrEmpty(serviceType) && !string.Equals(info.serviceType, serviceType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.IsNullOrWhiteSpace(info.host))
                            info.host = packet.RemoteEndPoint.Address.ToString();
                        var key = string.IsNullOrWhiteSpace(info.instanceId)
                            ? $"{info.serverId}@{info.host}"
                            : info.instanceId;
                        found[key] = info;
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
                }
            }

            return new List<PulletServerInfo>(found.Values);
        }
    }
}
