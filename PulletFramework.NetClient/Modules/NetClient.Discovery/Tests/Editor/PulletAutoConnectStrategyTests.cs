using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using PulletNet.ClientSDK;
using PulletNet.Discovery;
using PulletFramework.Discovery;
using System.Reflection;

namespace PulletFramework.NetClient.Discovery.Tests
{
    public sealed class PulletAutoConnectStrategyTests
    {
        [UnityTest] public IEnumerator DirectModeLogsChinese()
            => CheckModeLog(AutoConnectMode.Direct, "直接连接（Direct）", 0);
        [UnityTest] public IEnumerator DiscoverFirstModeLogsChinese()
            => CheckModeLog(AutoConnectMode.DiscoverFirst, "先发现再连接（DiscoverFirst）", 1);
        [UnityTest] public IEnumerator DirectThenDiscoverModeLogsChinese()
            => CheckModeLog(AutoConnectMode.DirectThenDiscover, "先直连，失败后发现（DirectThenDiscover）", 1);

        private static IEnumerator CheckModeLog(AutoConnectMode mode, string label, int expectedDiscoveries)
            => UnityTaskTest.Run(async () =>
        {
            var logs = new List<string>();
            int directCalls = 0, discoveries = 0;
            var strategy = new PulletAutoConnectStrategy(
                token => { directCalls++; return Task.FromResult(Failed()); },
                token => { discoveries++; return Task.FromResult<IReadOnlyList<DiscoveredServer>>(new[] { Server("selected") }); },
                (server, token) => Task.FromResult(ConnectionResult.Ok()),
                () => Task.CompletedTask, () => true, logs.Add, logs.Add);

            var result = await strategy.RunAsync(Options(mode), CancellationToken.None);

            Assert.That(logs[0], Does.Contain(label));
            Assert.That(directCalls, Is.EqualTo(mode == AutoConnectMode.DiscoverFirst ? 0 : 1));
            Assert.That(discoveries, Is.EqualTo(expectedDiscoveries));
            Assert.That(result.IsSuccess, Is.EqualTo(mode != AutoConnectMode.Direct));
        });

        [UnityTest]
        public IEnumerator DiscoveryWaitsForRealFailedAttemptRelease() => UnityTaskTest.Run(async () =>
        {
            var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int discoveryCalls = 0;
            var strategy = new PulletAutoConnectStrategy(token => Task.FromResult(Failed()),
                token => { discoveryCalls++; return Task.FromResult<IReadOnlyList<DiscoveredServer>>(new[] { Server("selected") }); },
                (server, token) => Task.FromResult(ConnectionResult.Ok()),
                () => { entered.TrySetResult(true); return released.Task; }, () => true, _ => { }, _ => { });
            var run = strategy.RunAsync(Options(AutoConnectMode.DirectThenDiscover), default); await entered.Task;
            try { Assert.That(run.IsCompleted, Is.False); Assert.That(discoveryCalls, Is.Zero); }
            finally { released.TrySetResult(true); await run; }
            Assert.That((await run).IsSuccess, Is.True); Assert.That(discoveryCalls, Is.EqualTo(1));
        });

        [Test]
        public void SelectPreferredServer_UsesStableName()
        {
            var servers = new[]
            {
                Server("Z"),
                Server("B"),
                Server("A")
            };

            DiscoveredServer selected = PulletAutoConnectStrategy.SelectPreferredServer(servers);

            Assert.That(selected.ServerName, Is.EqualTo("A"));
        }

        [UnityTest]
        public IEnumerator DiscoverFirst_DoesNotTryFixedEndpoint() => UnityTaskTest.Run(async () =>
        {
            int fixedCalls = 0;
            int discoveryCalls = 0;
            int discoveredConnectCalls = 0;
            var strategy = CreateStrategy(
                token => { fixedCalls++; return Task.FromResult(Failed()); },
                token =>
                {
                    discoveryCalls++;
                    return Task.FromResult<IReadOnlyList<DiscoveredServer>>(new[] { Server("BUS-01") });
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
        });

        [UnityTest]
        public IEnumerator DirectThenDiscover_ReleasesFailedFixedClientBeforeDiscovery() => UnityTaskTest.Run(async () =>
        {
            int releaseCalls = 0;
            var strategy = new PulletAutoConnectStrategy(
                token => Task.FromResult(Failed()),
                token => Task.FromResult<IReadOnlyList<DiscoveredServer>>(new[] { Server("BUS-01") }),
                (server, token) => Task.FromResult(ConnectionResult.Ok()),
                () => { releaseCalls++; return Task.CompletedTask; },
                () => true,
                _ => { },
                _ => { });

            ConnectionResult result = await strategy.RunAsync(
                Options(AutoConnectMode.DirectThenDiscover), CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(releaseCalls, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator DiscoveryDisabled_AlwaysUsesFixedEndpoint() => UnityTaskTest.Run(async () =>
        {
            int fixedCalls = 0;
            int discoveryCalls = 0;
            var strategy = CreateStrategy(
                token => { fixedCalls++; return Task.FromResult(ConnectionResult.Ok()); },
                token =>
                {
                    discoveryCalls++;
                    return Task.FromResult<IReadOnlyList<DiscoveredServer>>(Array.Empty<DiscoveredServer>());
                },
                (server, token) => Task.FromResult(Failed()));
            PulletAutoConnectStrategy.Options options = Options(AutoConnectMode.DiscoverFirst);
            options.EnableDiscovery = false;

            ConnectionResult result = await strategy.RunAsync(options, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixedCalls, Is.EqualTo(1));
            Assert.That(discoveryCalls, Is.Zero);
        });

        [UnityTest]
        public IEnumerator CanceledFixedConnection_DoesNotStartDiscovery() => UnityTaskTest.Run(async () =>
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
                    return Task.FromResult<IReadOnlyList<DiscoveredServer>>(Array.Empty<DiscoveredServer>());
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
        });

        private static PulletAutoConnectStrategy CreateStrategy(
            Func<CancellationToken, Task<ConnectionResult>> connectFixed,
            Func<CancellationToken, Task<IReadOnlyList<DiscoveredServer>>> discover,
            Func<DiscoveredServer, CancellationToken, Task<ConnectionResult>> connectDiscovered)
        {
            return new PulletAutoConnectStrategy(
                connectFixed,
                discover,
                connectDiscovered,
                () => Task.CompletedTask,
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

        private static DiscoveredServer Server(string name)
        {
            var advertisement = new DiscoveryAdvertisement
            {
                ServerName = name, InstanceId = name, Host = "127.0.0.1",
                TcpPort = 7778, UdpPort = 7777, WebPort = 7779
            };
            return (DiscoveredServer)typeof(DiscoveredServer).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(DiscoveryAdvertisement) }, null).Invoke(new object[] { advertisement });
        }

        private static ConnectionResult Failed()
            => ConnectionResult.Fail(ConnectionErrorCode.TransportUnavailable, "test failure");
    }
}
