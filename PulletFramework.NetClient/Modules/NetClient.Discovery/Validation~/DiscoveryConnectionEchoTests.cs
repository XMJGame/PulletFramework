using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Discovery.Tests
{
    // Isolated validation fixture only; never a dependency of the shipped test assembly.
    public sealed class DiscoveryConnectionEchoTests
    {
        [UnityTest] public IEnumerator Udp_DiscoverySelectsExactEndpointAndEchoes() => UnityTaskTest.Run(() => Exchange(ConnectionMode.UdpOnly));
        [UnityTest] public IEnumerator Tcp_DiscoverySelectsExactEndpointAndEchoes() => UnityTaskTest.Run(() => Exchange(ConnectionMode.TcpOnly));
        [UnityTest] public IEnumerator WebSocket_DiscoverySelectsExactEndpointAndEchoes() => UnityTaskTest.Run(() => Exchange(ConnectionMode.WebSocketOnly));
        private static async Task Exchange(ConnectionMode mode)
        {
            var path = Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS");
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            var ports = JsonUtility.FromJson<Ports>(File.ReadAllText(path));
            using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var owner = new GameObject("Discovery connection validation");
            var manager = owner.AddComponent<PulletNetworkManager>();
            manager.connectOnStart = false; manager.dontDestroyOnLoad = false;
            manager.enableConnectionLogs = false; manager.enableKeepAlive = false; manager.enableReconnect = false;
            manager.connectionMode = mode;
            manager.tcpPort = manager.udpPort = manager.webSocketPort = 0; // deterministic fixed-endpoint rejection before IO
            Call(manager, "Awake");
            var connector = owner.AddComponent<PulletDiscoveryConnector>();
            connector.enableLogs = false; connector.autoConnectMode = AutoConnectMode.DirectThenDiscover;
            connector.discoveryAddress = "127.0.0.1"; connector.discoveryPort = ((IPEndPoint)responder.Client.LocalEndPoint).Port;
            connector.discoveryTimeoutSeconds = .2f; connector.discoveryMaxAttempts = 1;
            connector.discoverySendIntervalSeconds = .05f; connector.retryDiscoveryUntilConnected = false;
            Call(connector, "Awake");
            var reply = Reply(responder, ports);
            var payload = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.PayloadReceived += p => payload.TrySetResult(p.Memory.ToArray());
            try
            {
                var connect = connector.AutoConnectAsync();
                await Until(() => connect.IsCompleted, manager, connector);
                var result = await connect;
                Assert.That(result.IsSuccess, Is.True, result.Message);
                await reply;
                Assert.That(manager.ActiveEndpoint.Host, Is.EqualTo("127.0.0.1"));
                Assert.That(manager.ActiveEndpoint.TcpPort, Is.EqualTo(ports.tcp));
                Assert.That(manager.ActiveEndpoint.UdpPort, Is.EqualTo(ports.udp));
                Assert.That(manager.ActiveEndpoint.WebSocketPort, Is.EqualTo(ports.ws));
                Assert.That(connector.LastDiscoveredServers.Count, Is.EqualTo(1));
                var send = await manager.SendAsync(new byte[] { 91, (byte)mode });
                Assert.That(send.IsSuccess, Is.True, send.Message);
                await Until(() => payload.Task.IsCompleted, manager, connector);
                Assert.That(await payload.Task, Is.EqualTo(new byte[] { 91, (byte)mode }));
            }
            finally
            {
                responder.Dispose();
                try { await reply; } catch (ObjectDisposedException) { } catch (SocketException) { }
                try { await connector.CloseAsync(); }
                finally { try { await manager.ShutdownAsync(); } finally { Object.DestroyImmediate(owner); } }
            }
        }
        private static async Task Reply(UdpClient responder, Ports ports)
        {
            var request = await responder.ReceiveAsync();
            var match = Regex.Match(Encoding.UTF8.GetString(request.Buffer), "\"requestId\"\\s*:\\s*\"([^\"]+)\"");
            Assert.That(match.Success, Is.True);
            string json = "{\"magic\":\"PULLET_DISCOVERY\",\"version\":1,\"requestId\":\"" + match.Groups[1].Value
                + "\",\"instanceId\":\"reference\",\"serverName\":\"reference\",\"serviceType\":\"pulletnet\",\"host\":\"0.0.0.0\","
                + "\"tcpPort\":" + ports.tcp + ",\"udpPort\":" + ports.udp + ",\"webPort\":" + ports.ws + "}";
            var bytes = Encoding.UTF8.GetBytes(json);
            await responder.SendAsync(bytes, bytes.Length, request.RemoteEndPoint);
        }
        private static async Task Until(Func<bool> done, PulletNetworkManager manager, PulletDiscoveryConnector connector)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(10), "Discovery/connection/echo did not settle.");
                Call(manager, "Update"); Call(connector, "Update"); await Task.Yield();
            }
            Call(manager, "Update"); Call(connector, "Update");
        }
        private static void Call(object owner, string method) => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
        [Serializable] private sealed class Ports { public int tcp; public int udp; public int ws; }
    }
}
