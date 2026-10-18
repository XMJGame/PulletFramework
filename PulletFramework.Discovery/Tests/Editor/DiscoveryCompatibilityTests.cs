using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.Discovery;
using System.Collections;
using System.Diagnostics;
using UnityEngine.TestTools;

namespace PulletFramework.Discovery.Tests
{
    public sealed class DiscoveryCompatibilityTests
    {
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator CancellationAfterBroadcastSettlesReceiveAsOwnedCancellation() => Run(async () =>
        {
            using var passive = CreateResponder(); using var cancellation = new CancellationTokenSource();
            var sent = passive.ReceiveAsync();
            var run = DiscoverCore(LocalEndpoint(passive), 30, 1, .1f, "test", null, cancellation.Token);
            await sent;
            cancellation.Cancel();
            try { await run; Assert.Fail("The canceled owner must not report a successful round."); }
            catch (OperationCanceledException error) { Assert.That(error.CancellationToken.IsCancellationRequested, Is.True); }
            Assert.That(run.IsCompleted, Is.True);
        });

        [UnityTest]
        public IEnumerator DiscoverCore_ReturnsMatchingServerAndUsesReplyAddressForWildcardHost() => Run(async () =>
        {
            using (var responder = CreateResponder())
            {
                Task reply = ReplyOnceAsync(responder, new Reply("instance-a", "xrvehicle", "0.0.0.0"));
                var servers = await Discover(responder, "xrvehicle");
                await reply;

                Assert.That(servers.Count, Is.EqualTo(1));
                Assert.That(servers[0].InstanceId, Is.EqualTo("instance-a"));
                Assert.That(servers[0].Host, Is.EqualTo(IPAddress.Loopback.ToString()));
            }
        });

        [UnityTest]
        public IEnumerator DiscoverCore_IgnoresDifferentServiceType() => Run(async () =>
        {
            using (var responder = CreateResponder())
            {
                Task reply = ReplyOnceAsync(responder, new Reply("instance-a", "other", "127.0.0.1"));
                var servers = await Discover(responder, "xrvehicle");
                await reply;

                Assert.That(servers, Is.Empty);
            }
        });

        [UnityTest]
        public IEnumerator DiscoverCore_AggregatesAndReplacesRepeatedInstance() => Run(async () =>
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
                Assert.That(Find(servers, "instance-a").ServerName, Is.EqualTo("updated"));
                Assert.That(Find(servers, "instance-b").ServerName, Is.EqualTo("second"));
            }
        });

        [UnityTest]
        public IEnumerator DiscoverCore_CancellationThrows() => Run(async () =>
        {
            using (var passive = CreateResponder())
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(50);
                try
                {
                    await DiscoverCore(
                        LocalEndpoint(passive), 1f, 1, 0.05f, "xrvehicle", null, cancellation.Token);
                    Assert.Fail("Cancellation must be reported to the caller.");
                }
                catch (OperationCanceledException) { }
            }
        });

        [UnityTest]
        public IEnumerator DiscoverAsync_RejectsInvalidPort() => Run(async () =>
        {
            try
            {
                await DiscoverCore(new IPEndPoint(IPAddress.Loopback, 0), .1f, 1, .05f, "xrvehicle", null);
                Assert.Fail("An invalid UDP port must be rejected.");
            }
            catch (ArgumentException) { }
        });

        [UnityTest]
        public IEnumerator DiscoverCore_TimeoutReturnsEmpty() => Run(async () =>
        {
            using (var passive = CreateResponder())
            {
                var servers = await DiscoverCore(
                    LocalEndpoint(passive), 0.15f, 1, 0.05f, "xrvehicle", null);
                Assert.That(servers, Is.Empty);
            }
        });

        private static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var clock = Stopwatch.StartNew();
            while (!task.IsCompleted) { Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(10)); yield return null; }
            task.GetAwaiter().GetResult();
        }

        private static async Task<IReadOnlyList<DiscoveredServer>> DiscoverCore(IPEndPoint destination,
            float timeout, int attempts, float interval, string service, Action<int, int> attempted,
            CancellationToken token = default)
        {
            await using var runtime = new DiscoveryRuntime();
            // Progress is a UI fact now; observe actual loopback IO independently below.
            var options = new DiscoveryOptions { Destination = destination, Timeout = TimeSpan.FromSeconds(timeout),
                MaximumAttempts = attempts, SendInterval = TimeSpan.FromSeconds(interval), ServiceType = service };
            var work = runtime.DiscoverAsync(options, token);
            if (attempted != null)
            {
                runtime.Attempted += (_, a, b) => attempted(a, b);
                while (!work.IsCompleted && runtime.PendingNotices == 0) await Task.Yield();
                // Pump until the requested progress fact is delivered; bounded by caller cancellation.
                while (!work.IsCompleted && runtime.IsDiscovering)
                {
                    runtime.Update();
                    if (runtime.PendingNotices == 0) break;
                    await Task.Yield();
                }
            }
            return (await work).Servers;
        }

        private static Task<IReadOnlyList<DiscoveredServer>> Discover(UdpClient responder, string serviceType)
            => DiscoverCore(
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

        private static DiscoveredServer Find(IReadOnlyList<DiscoveredServer> servers, string instanceId)
        {
            foreach (DiscoveredServer server in servers)
                if (server.InstanceId == instanceId)
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
