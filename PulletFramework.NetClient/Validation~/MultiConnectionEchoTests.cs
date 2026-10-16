using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Tests
{
    // Public API only. Copy into Tests/Editor in the fresh isolated validation package.
    public sealed class MultiConnectionEchoTests
    {
        private PulletClientRuntime _runtime;
        [UnityTearDown] public IEnumerator TearDown() => UnityTaskTest.Run(async () =>
        {
            if (_runtime == null) return;
            var close = _runtime.CloseAsync(); await Wait(close, false, "teardown");
            Assert.That(close.Result.HostCleanupError, Is.Null);
            foreach (var result in close.Result.Clients) Assert.That(result.CleanupError, Is.Null);
            _runtime = null;
        });

        [UnityTest]
        public IEnumerator IndependentSourcesRemainCorrectAfterRemovingAndReusingOneName() => UnityTaskTest.Run(async () =>
        {
            _runtime = new PulletClientRuntime();
            var a = _runtime.Create("a", Options(ConnectionMode.UdpOnly));
            var b = _runtime.Create("b", Options(ConnectionMode.TcpOnly));
            var received = new List<(HostedClient Source, byte? Id, byte[] Bytes)>();
            _runtime.PayloadReceived += e => received.Add((e.Source, e.Payload.ChannelId, e.Payload.Memory.ToArray()));
            var connectA = a.Client.ConnectAsync(); var connectB = b.Client.ConnectAsync();
            await Wait(Task.WhenAll(connectA, connectB), phase: "connect pair");
            Assert.That(connectA.Result.IsSuccess, Is.True, connectA.Result.Message);
            Assert.That(connectB.Result.IsSuccess, Is.True, connectB.Result.Message);
            var sendA = a.Client.SendAsync(new byte[] { 42 }, 7).AsTask();
            var sendB = b.Client.SendAsync(new byte[] { 42 }, 7).AsTask();
            await Wait(Task.WhenAll(sendA, sendB), phase: "send pair");
            Assert.That(sendA.Result.IsSuccess && sendB.Result.IsSuccess, Is.True);
            await Until(() => received.Count == 2);
            Assert.That(received.Exists(e => ReferenceEquals(e.Source, a) && e.Id == 7 && e.Bytes[0] == 42), Is.True);
            Assert.That(received.Exists(e => ReferenceEquals(e.Source, b) && e.Id == 7 && e.Bytes[0] == 42), Is.True);
            var remove = _runtime.RemoveAsync(a); await Wait(remove, phase: "remove UDP source"); Assert.That(remove.Result.IsSuccess, Is.True);
            Assert.That(b.Client.State, Is.EqualTo(ConnectionState.Connected));
            var current = _runtime.Create("a", Options(ConnectionMode.UdpOnly));
            Assert.That(current, Is.Not.SameAs(a));
            var next = current.Client.ConnectAsync(); await Wait(next, phase: "replacement connect"); Assert.That(next.Result.IsSuccess, Is.True, next.Result.Message);
            var nextSend = current.Client.SendAsync(new byte[] { 43 }, 7).AsTask();
            sendB = b.Client.SendAsync(new byte[] { 44 }, 7).AsTask();
            await Wait(Task.WhenAll(nextSend, sendB), phase: "replacement send");
            Assert.That(nextSend.Result.IsSuccess && sendB.Result.IsSuccess, Is.True);
            await Until(() => received.Count == 4);
            Assert.That(received.Exists(e => ReferenceEquals(e.Source, current) && e.Bytes[0] == 43), Is.True);
            Assert.That(received.Exists(e => ReferenceEquals(e.Source, b) && e.Bytes[0] == 44), Is.True);
            Assert.Throws<InvalidOperationException>(() => _runtime.RemoveAsync(a));
            var close = _runtime.CloseAsync(); await Wait(close, false, "close connected pair"); Assert.That(close.Result.IsSuccess, Is.True);
            Assert.That(current.Client.State, Is.EqualTo(ConnectionState.Disposed)); Assert.That(b.Client.State, Is.EqualTo(ConnectionState.Disposed));
        });

        [UnityTest]
        public IEnumerator ReliableOverflowRemovesOnlyOneRealConnectionWithoutFramePumping() => UnityTaskTest.Run(async () =>
        {
            _runtime = new PulletClientRuntime(new PulletClientRuntimeOptions { PayloadsPerClient = 1 });
            var a = _runtime.Create("a", Options(ConnectionMode.UdpOnly)); var b = _runtime.Create("b", Options(ConnectionMode.TcpOnly));
            var connectA = a.Client.ConnectAsync(); var connectB = b.Client.ConnectAsync();
            await Wait(Task.WhenAll(connectA, connectB), phase: "overflow connect");
            Assert.That(connectA.Result.IsSuccess && connectB.Result.IsSuccess, Is.True);
            _runtime.Update();
            var first = a.Client.SendAsync(new byte[] { 51 }, 7).AsTask();
            var second = a.Client.SendAsync(new byte[] { 52 }, 7).AsTask();
            await Wait(Task.WhenAll(first, second), false, "overflow send");
            Assert.That(first.Result.IsSuccess && second.Result.IsSuccess, Is.True);
            await Until(() => !_runtime.TryGetClient("a", out _), false);
            var remove = _runtime.RemoveAsync(a); await Wait(remove, false, "overflow cleanup");
            Assert.That(remove.Result.DeliveryFailure.Reason, Is.EqualTo(PulletClientDeliveryFailureReason.ReliableOverflow));
            Assert.That(remove.Result.CleanupError, Is.Null); Assert.That(a.Client.State, Is.EqualTo(ConnectionState.Disposed));
            Assert.That(b.Client.State, Is.EqualTo(ConnectionState.Connected));
            int failures = 0; int replies = 0;
            _runtime.DeliveryFaulted += e => { if (ReferenceEquals(e.Source, a)) failures++; };
            _runtime.PayloadReceived += e => { if (ReferenceEquals(e.Source, b) && e.Payload.Memory.Span[0] == 53) replies++; };
            _runtime.Update();
            var send = b.Client.SendAsync(new byte[] { 53 }, 7).AsTask(); await Wait(send); Assert.That(send.Result.IsSuccess, Is.True);
            await Until(() => replies == 1); Assert.That(failures, Is.EqualTo(1));
            Assert.That(_runtime.GetSnapshot().PayloadBytes, Is.Zero);
        });

        private static NetClientOptions Options(ConnectionMode mode)
        {
            var path = Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS");
            Assert.That(string.IsNullOrEmpty(path), Is.False, "Controlled server ports required.");
            var ports = JsonUtility.FromJson<Ports>(File.ReadAllText(path));
            return new NetClientOptions
            {
                Connection = new ConnectionOptions { Host = "127.0.0.1", ConnectionMode = mode, UdpPort = ports.udp, TcpPort = ports.tcp, WsPort = ports.ws },
                LogicalChannels = new Dictionary<byte, ChannelType> { { 0, ChannelType.ReliableOrdered }, { 7, ChannelType.ReliableOrdered } },
                KeepAlive = new KeepAliveOptions { EnableKeepAlive = false }, Reconnect = new ReconnectOptions { EnableReconnect = false }
            };
        }
        private async Task Wait(Task task, bool pump = true, string phase = "operation")
        {
            UnityEngine.Debug.Log("MultiConnection: " + phase + " started");
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (pump) _runtime.Update();
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), phase + " has not settled."); await Task.Yield();
            }
            task.GetAwaiter().GetResult();
            UnityEngine.Debug.Log("MultiConnection: " + phase + " completed");
        }
        private async Task Until(Func<bool> condition, bool pump = true)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (pump) _runtime.Update();
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Expected source observation not reached."); await Task.Yield();
            }
        }
        [Serializable] private sealed class Ports { public int udp; public int tcp; public int ws; }
    }
}
