using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletAutoConnectStrategyTests
    {
        [Test]
        public void SelectPreferredServer_UsesStableName()
        {
            var servers = new[]
            {
                Server("Z"),
                Server("B"),
                Server("A")
            };

            PulletServerInfo selected = PulletAutoConnectStrategy.SelectPreferredServer(servers);

            Assert.That(selected.serverName, Is.EqualTo("A"));
        }

        [Test]
        public async Task DiscoverFirst_DoesNotTryFixedEndpoint()
        {
            int fixedCalls = 0;
            int discoveryCalls = 0;
            int discoveredConnectCalls = 0;
            var strategy = CreateStrategy(
                token => { fixedCalls++; return Task.FromResult(Failed()); },
                token =>
                {
                    discoveryCalls++;
                    return Task.FromResult<IReadOnlyList<PulletServerInfo>>(new[] { Server("BUS-01") });
                },
                (server, token) =>
                {
                    discoveredConnectCalls++;
                    return Task.FromResult(ConnectionResult.Ok());
                });

            ConnectionResult result = await strategy.RunAsync(Options(AutoConnectMode.DiscoverFirst), CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixedCalls, Is.Zero);
            Assert.That(discoveryCalls, Is.EqualTo(1));
            Assert.That(discoveredConnectCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task DirectThenDiscover_ReleasesFailedFixedClientBeforeDiscovery()
        {
            int releaseCalls = 0;
            var strategy = new PulletAutoConnectStrategy(
                token => Task.FromResult(Failed()),
                token => Task.FromResult<IReadOnlyList<PulletServerInfo>>(new[] { Server("BUS-01") }),
                (server, token) => Task.FromResult(ConnectionResult.Ok()),
                () => releaseCalls++,
                () => true,
                _ => { },
                _ => { });

            ConnectionResult result = await strategy.RunAsync(
                Options(AutoConnectMode.DirectThenDiscover), CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(releaseCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task DiscoveryDisabled_AlwaysUsesFixedEndpoint()
        {
            int fixedCalls = 0;
            int discoveryCalls = 0;
            var strategy = CreateStrategy(
                token => { fixedCalls++; return Task.FromResult(ConnectionResult.Ok()); },
                token =>
                {
                    discoveryCalls++;
                    return Task.FromResult<IReadOnlyList<PulletServerInfo>>(Array.Empty<PulletServerInfo>());
                },
                (server, token) => Task.FromResult(Failed()));
            PulletAutoConnectStrategy.Options options = Options(AutoConnectMode.DiscoverFirst);
            options.EnableDiscovery = false;

            ConnectionResult result = await strategy.RunAsync(options, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixedCalls, Is.EqualTo(1));
            Assert.That(discoveryCalls, Is.Zero);
        }

        [Test]
        public async Task CanceledFixedConnection_DoesNotStartDiscovery()
        {
            using var cancellation = new CancellationTokenSource();
            int discoveryCalls = 0;
            var strategy = CreateStrategy(
                token =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(ConnectionResult.Fail(
                        ConnectionErrorCode.Canceled, "Connection was canceled."));
                },
                token =>
                {
                    discoveryCalls++;
                    return Task.FromResult<IReadOnlyList<PulletServerInfo>>(Array.Empty<PulletServerInfo>());
                },
                (server, token) => Task.FromResult(ConnectionResult.Ok()));

            bool wasCanceled = false;
            try
            {
                await strategy.RunAsync(Options(AutoConnectMode.DirectThenDiscover), cancellation.Token);
            }
            catch (OperationCanceledException) { wasCanceled = true; }
            Assert.That(wasCanceled, Is.True);
            Assert.That(discoveryCalls, Is.Zero);
        }

        private static PulletAutoConnectStrategy CreateStrategy(
            Func<CancellationToken, Task<ConnectionResult>> connectFixed,
            Func<CancellationToken, Task<IReadOnlyList<PulletServerInfo>>> discover,
            Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> connectDiscovered)
        {
            return new PulletAutoConnectStrategy(
                connectFixed,
                discover,
                connectDiscovered,
                () => { },
                () => true,
                _ => { },
                _ => { });
        }

        private static PulletAutoConnectStrategy.Options Options(AutoConnectMode mode)
        {
            return new PulletAutoConnectStrategy.Options
            {
                Mode = mode,
                EnableDiscovery = true,
                RetryDiscoveryUntilConnected = false,
                DiscoveryRetrySeconds = 0.25f,
                FixedEndpointDescription = "127.0.0.1",
                DiscoveryDescription = "test"
            };
        }

        private static PulletServerInfo Server(string name)
        {
            return new PulletServerInfo
            {
                serverName = name,
                instanceId = name,
                host = "127.0.0.1",
                tcpPort = 7778,
                udpPort = 7777,
                webPort = 7779
            };
        }

        private static ConnectionResult Failed()
            => ConnectionResult.Fail(ConnectionErrorCode.TransportUnavailable, "test failure");
    }
}
