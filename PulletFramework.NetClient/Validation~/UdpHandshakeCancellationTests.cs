using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Tests
{
    // Public API, real UDP socket. A listening peer deliberately never answers.
    public sealed class UdpHandshakeCancellationTests
    {
        [UnityTest] public IEnumerator TimeoutJoinsTheOutstandingPhysicalRead() => UnityTaskTest.Run(() => Connect(false));
        [UnityTest] public IEnumerator CallerCancellationJoinsTheOutstandingPhysicalRead() => UnityTaskTest.Run(() => Connect(true));
        private static async Task Connect(bool cancel)
        {
            using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var host = new NetClientHost();
            var source = host.Create("blackhole", new NetClientOptions
            {
                Connection = new ConnectionOptions { Host = "127.0.0.1", ConnectionMode = ConnectionMode.UdpOnly, UdpPort = ((IPEndPoint)peer.Client.LocalEndPoint).Port },
                Handshake = new HandshakeOptions { HandshakeTimeoutMs = 100, UdpHelloRetryIntervalMs = 40 },
                KeepAlive = new KeepAliveOptions { EnableKeepAlive = false }, Reconnect = new ReconnectOptions { EnableReconnect = false }
            });
            try
            {
                using var cancellation = new CancellationTokenSource();
                var connect = source.Client.ConnectAsync(cancellation.Token);
                // Observe the real Request before canceling, rather than relying
                // on a delay to guess whether a physical receive was started.
                var request = await peer.ReceiveAsync(); Assert.That(request.Buffer.Length, Is.GreaterThan(0));
                if (cancel) cancellation.Cancel();
                var result = await connect;
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(cancel ? ConnectionErrorCode.Canceled : ConnectionErrorCode.Timeout));
                Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disconnected));
                Assert.That((await host.RemoveAsync(source)).IsSuccess, Is.True);
            }
            finally { Assert.That((await host.CloseAllAsync()).IsSuccess, Is.True); }
        }
    }
}
