using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletServerDiscoveryTests
    {
        [Test]
        public void DiscoverCore_ReturnsMatchingServerAndUsesReplyAddressForWildcardHost() => Run(async () =>
        {
            using (var responder = CreateResponder())
            {
                Task reply = ReplyOnceAsync(responder, new Reply("instance-a", "xrvehicle", "0.0.0.0"));
                var servers = await Discover(responder, "xrvehicle");
                await reply;

                Assert.That(servers.Count, Is.EqualTo(1));
                Assert.That(servers[0].instanceId, Is.EqualTo("instance-a"));
                Assert.That(servers[0].host, Is.EqualTo(IPAddress.Loopback.ToString()));
            }
        });

        [Test]
        public void DiscoverCore_IgnoresDifferentServiceType() => Run(async () =>
        {
            using (var responder = CreateResponder())
            {
                Task reply = ReplyOnceAsync(responder, new Reply("instance-a", "other", "127.0.0.1"));
                var servers = await Discover(responder, "xrvehicle");
                await reply;

                Assert.That(servers, Is.Empty);
            }
        });

        [Test]
        public void DiscoverCore_AggregatesAndReplacesRepeatedInstance() => Run(async () =>
        {
            using (var responder = CreateResponder())
            {
                Task reply = ReplyOnceAsync(responder,
                    new Reply("instance-a", "xrvehicle", "127.0.0.1", "old"),
                    new Reply("instance-b", "xrvehicle", "127.0.0.1", "second"),
                    new Reply("instance-a", "xrvehicle", "127.0.0.1", "updated"));
                var servers = await Discover(responder, "xrvehicle");
                await reply;

                Assert.That(servers.Count, Is.EqualTo(2));
                Assert.That(Find(servers, "instance-a").serverName, Is.EqualTo("updated"));
                Assert.That(Find(servers, "instance-b").serverName, Is.EqualTo("second"));
            }
        });

        [Test]
        public void DiscoverCore_CancellationThrows() => Run(async () =>
        {
            using (var passive = CreateResponder())
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(50);
                try
                {
                    await PulletServerDiscovery.DiscoverCoreAsync(
                        LocalEndpoint(passive), 1f, 1, 0.05f, "xrvehicle", null, cancellation.Token);
                    Assert.Fail("Cancellation must be reported to the caller.");
                }
                catch (OperationCanceledException) { }
            }
        });

        [Test]
        public void DiscoverAsync_RejectsInvalidPort() => Run(async () =>
        {
            try
            {
                await PulletServerDiscovery.DiscoverAsync(0, 0.1f, "xrvehicle");
                Assert.Fail("An invalid UDP port must be rejected.");
            }
            catch (ArgumentOutOfRangeException) { }
        });

        [Test]
        public void DiscoverCore_TimeoutReturnsEmpty() => Run(async () =>
        {
            using (var passive = CreateResponder())
            {
                var servers = await PulletServerDiscovery.DiscoverCoreAsync(
                    LocalEndpoint(passive), 0.15f, 1, 0.05f, "xrvehicle", null);
                Assert.That(servers, Is.Empty);
            }
        });

        private static void Run(Func<Task> body)
            => Task.Run(body).GetAwaiter().GetResult();

        private static Task<IReadOnlyList<PulletServerInfo>> Discover(UdpClient responder, string serviceType)
            => PulletServerDiscovery.DiscoverCoreAsync(
                LocalEndpoint(responder), 0.4f, 1, 0.05f, serviceType, null);

        private static UdpClient CreateResponder()
            => new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        private static IPEndPoint LocalEndpoint(UdpClient client)
            => (IPEndPoint)client.Client.LocalEndPoint;

        private static async Task ReplyOnceAsync(UdpClient responder, params Reply[] replies)
        {
            UdpReceiveResult request = await responder.ReceiveAsync();
            string json = Encoding.UTF8.GetString(request.Buffer);
            Match match = Regex.Match(json, @"""requestId""\s*:\s*""([^""]+)""");
            Assert.That(match.Success, Is.True, json);
            string requestId = match.Groups[1].Value;

            foreach (Reply reply in replies)
            {
                string response = "{\"magic\":\"PULLET_DISCOVERY\",\"version\":1," +
                                  "\"requestId\":\"" + requestId + "\"," +
                                  "\"instanceId\":\"" + reply.InstanceId + "\"," +
                                  "\"serverId\":\"test-server\"," +
                                  "\"serverName\":\"" + reply.Name + "\"," +
                                  "\"serviceType\":\"" + reply.ServiceType + "\"," +
                                  "\"host\":\"" + reply.Host + "\"," +
                                  "\"tcpPort\":7778,\"udpPort\":7777,\"webPort\":7779}";
                byte[] bytes = Encoding.UTF8.GetBytes(response);
                await responder.SendAsync(bytes, bytes.Length, request.RemoteEndPoint);
            }
        }

        private static PulletServerInfo Find(IReadOnlyList<PulletServerInfo> servers, string instanceId)
        {
            foreach (PulletServerInfo server in servers)
                if (server.instanceId == instanceId)
                    return server;
            throw new AssertionException("Missing discovery instance: " + instanceId);
        }

        private sealed class Reply
        {
            public readonly string InstanceId;
            public readonly string ServiceType;
            public readonly string Host;
            public readonly string Name;

            public Reply(string instanceId, string serviceType, string host, string name = "test")
            {
                InstanceId = instanceId;
                ServiceType = serviceType;
                Host = host;
                Name = name;
            }
        }
    }
}
