using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Tests
{
    // Native public API control: no Unity adapter, dispatch queue or injected transport.
    public sealed class SdkHostUnityCleanupTests
    {
        private NetClientHost _host;
        [UnityTest] public IEnumerator RemoveConnectedUdp() => UnityTaskTest.Run(() => Exchange(false, false));
        [UnityTest] public IEnumerator RemoveUdpAfterEcho() => UnityTaskTest.Run(() => Exchange(true, false));
        [UnityTest] public IEnumerator DisconnectUdpAfterEcho() => UnityTaskTest.Run(() => Exchange(true, true));
        [UnityTearDown] public IEnumerator TearDown() => UnityTaskTest.Run(async () =>
        {
            if (_host == null) return;
            var close = _host.CloseAllAsync(); await Observe(close, "host teardown");
            Assert.That(close.Result.IsSuccess, Is.True); _host = null;
        });
        private async Task Exchange(bool echo, bool disconnect)
        {
            var ports = JsonUtility.FromJson<Ports>(File.ReadAllText(Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS")));
            _host = new NetClientHost();
            var source = _host.Create("control", new NetClientOptions
            {
                Connection = new ConnectionOptions { Host = "127.0.0.1", ConnectionMode = ConnectionMode.UdpOnly, UdpPort = ports.udp },
                KeepAlive = new KeepAliveOptions { EnableKeepAlive = false }, Reconnect = new ReconnectOptions { EnableReconnect = false }
            });
            var connect = source.Client.ConnectAsync(); await Observe(connect, "connect"); Assert.That(connect.Result.IsSuccess, Is.True);
            if (echo)
            {
                var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _host.PayloadReceived += e => received.TrySetResult(true);
                var send = source.Client.SendAsync(new byte[] { 71 }, 0).AsTask(); await Observe(send, "send");
                Assert.That(send.Result.IsSuccess, Is.True); await Observe(received.Task, "echo");
            }
            if (disconnect)
            {
                var stopped = source.Client.DisconnectAsync(); await Observe(stopped, "disconnect");
                Assert.That(stopped.Result.IsSuccess, Is.True);
            }
            var remove = _host.RemoveAsync(source); await Observe(remove, "remove");
            Assert.That(remove.Result.IsSuccess, Is.True);
        }
        private static async Task Observe(Task task, string phase)
        {
            UnityEngine.Debug.Log("SdkHostControl: " + phase + " started");
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), phase + " did not settle.");
                await Task.Yield();
            }
            task.GetAwaiter().GetResult();
            UnityEngine.Debug.Log("SdkHostControl: " + phase + " completed");
        }
        [Serializable] private sealed class Ports { public int udp; }
    }
}
