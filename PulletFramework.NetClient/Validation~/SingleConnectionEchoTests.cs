using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Tests
{
    // Copied into the package's Tests/Editor only in an isolated validation project.
    // No NetCore DLL or external server dependency is added to the shipped Unity assembly.
    public sealed class SingleConnectionEchoTests
    {
        [UnityTest] public IEnumerator Udp_ConnectSendReconnectAndDisconnect() => Exchange(ConnectionMode.UdpOnly);
        [UnityTest] public IEnumerator Tcp_ConnectSendReconnectAndDisconnect() => Exchange(ConnectionMode.TcpOnly);
        [UnityTest] public IEnumerator WebSocket_ConnectSendReconnectAndDisconnect() => Exchange(ConnectionMode.WebSocketOnly);

        private static IEnumerator Exchange(ConnectionMode mode)
        {
            string config = Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS");
            Assert.That(string.IsNullOrEmpty(config), Is.False, "Set PULLETNET_U2_PORTS to the controlled echo server's ports.json.");
            var ports = JsonUtility.FromJson<Ports>(File.ReadAllText(config));
            var gameObject = new GameObject("Isolated single connection");
            var manager = gameObject.AddComponent<PulletNetworkManager>();
            manager.connectOnStart = false;
            manager.dontDestroyOnLoad = false;
            manager.enableConnectionLogs = false;
            manager.enableDiscovery = false;
            manager.enableKeepAlive = false;
            manager.enableReconnect = false;
            manager.connectionMode = mode;
            manager.udpPort = ports.udp;
            manager.tcpPort = ports.tcp;
            manager.webSocketPort = ports.ws;
            var received = new List<(byte? Id, byte[] Bytes, ChannelType Mode, TransportKind Transport)>();
            var sessions = new List<uint>();
            manager.PayloadReceived += p => received.Add((p.ChannelId, p.Memory.ToArray(), p.ChannelType, p.TransportType));
            manager.Connected += e => sessions.Add(e.SessionId);
            // EditMode does not run MonoBehaviour lifecycle automatically.
            if (typeof(PulletNetworkManager).GetField("_lifetimeCts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager) == null)
                Invoke(manager, "Awake");
            try
            {
                Task<ConnectionResult> connect = manager.ConnectAsync();
                yield return Wait(connect, manager);
                Assert.That(connect.Result.IsSuccess, Is.True, connect.Result.Message);
                Assert.That(manager.IsConnected, Is.True);
                Invoke(manager, "Update");
                Assert.That(sessions.Count, Is.EqualTo(1));

                Task<SendResult> send = manager.SendAsync(new byte[] { 11, 42 }).AsTask();
                yield return Wait(send, manager);
                Assert.That(send.Result.IsSuccess, Is.True, send.Result.Message);
                yield return WaitForPayload(received, 1, manager);
                Assert.That(received[0].Id, Is.EqualTo((byte)0));
                Assert.That(received[0].Bytes, Is.EqualTo(new byte[] { 11, 42 }));
                Assert.That(received[0].Mode, Is.EqualTo(ChannelType.ReliableOrdered));
                Assert.That(received[0].Transport, Is.EqualTo(mode == ConnectionMode.UdpOnly ? TransportKind.Udp : mode == ConnectionMode.TcpOnly ? TransportKind.Tcp : TransportKind.WebSocket));

                byte explicitId = mode == ConnectionMode.UdpOnly ? (byte)1 : (byte)0;
                send = manager.SendOnChannelAsync(new byte[] { 12, explicitId }, explicitId).AsTask();
                yield return Wait(send, manager);
                Assert.That(send.Result.IsSuccess, Is.True, send.Result.Message);
                yield return WaitForPayload(received, 2, manager);
                Assert.That(received[1].Id, Is.EqualTo(explicitId));
                Assert.That(received[1].Bytes, Is.EqualTo(new byte[] { 12, explicitId }));
                Assert.That(received[1].Mode, Is.EqualTo(mode == ConnectionMode.UdpOnly ? ChannelType.ReliableUnordered : ChannelType.ReliableOrdered));
                send = manager.SendOnChannelAsync(new byte[] { 13 }, 31).AsTask();
                yield return Wait(send, manager);
                Assert.That(send.Result.IsSuccess, Is.False, "Unnegotiated ID must not silently fall back.");
                if (mode != ConnectionMode.UdpOnly)
                {
                    send = manager.SendAsync(new byte[] { 13 }, ChannelType.Unreliable).AsTask();
                    yield return Wait(send, manager);
                    Assert.That(send.Result.IsSuccess, Is.False, "TCP/WS must not silently change delivery semantics.");
                }

                Task<ConnectionResult> disconnect = manager.DisconnectAsync();
                yield return Wait(disconnect, manager);
                Assert.That(disconnect.Result.IsSuccess, Is.True, disconnect.Result.Message);
                Assert.That(manager.IsConnected, Is.False);
                connect = manager.ConnectAsync();
                yield return Wait(connect, manager);
                Assert.That(connect.Result.IsSuccess, Is.True, connect.Result.Message);
                Invoke(manager, "Update");
                Assert.That(sessions.Count, Is.EqualTo(2));
                Assert.That(sessions[1], Is.Not.EqualTo(sessions[0]), "Fresh connect creates a new Session.");
                send = manager.SendAsync(new byte[] { 14 }).AsTask();
                yield return Wait(send, manager);
                Assert.That(send.Result.IsSuccess, Is.True);
                yield return WaitForPayload(received, 3, manager);
                Assert.That(received[2].Bytes, Is.EqualTo(new byte[] { 14 }));
                disconnect = manager.DisconnectAsync();
                yield return Wait(disconnect, manager);
                Assert.That(disconnect.Result.IsSuccess, Is.True);
                Assert.That(manager.SendAsync(new byte[] { 15 }).GetAwaiter().GetResult().ErrorCode, Is.EqualTo(SendErrorCode.NotConnected));
            }
            finally
            {
                // Release the actual SDK owner even when an assertion fails; do
                // not leave a background network instance for the next test.
                Invoke(manager, "DisposeClient");
                Object.DestroyImmediate(gameObject);
            }
        }

        private static IEnumerator Wait(Task task, PulletNetworkManager manager)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Invoke(manager, "Update");
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Unity operation did not complete.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator WaitForPayload(ICollection received, int count, PulletNetworkManager manager)
        {
            var watch = Stopwatch.StartNew();
            while (received.Count < count)
            {
                Invoke(manager, "Update");
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Echo did not reach Unity main-thread dispatch.");
                yield return null;
            }
        }

        private static void Invoke(object owner, string method)
            => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);

        [Serializable] private sealed class Ports { public int udp; public int tcp; public int ws; }
    }
}
