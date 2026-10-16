using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletActiveServerRecoveryTests
    {
        [UnityTest]
        public IEnumerator Recovery_AttemptsTheExactSelectedEndpoint() => UnityTaskTest.Run(async () =>
        {
            PulletServerInfo connectedTarget = null;
            var attempted = new TaskCompletionSource<bool>();
            using (var recovery = CreateRecovery(
                       (target, token) =>
                       {
                           connectedTarget = target;
                           attempted.TrySetResult(true);
                           return Task.FromResult(ConnectionResult.Ok());
                       },
                       _ => { }))
            {
                PulletServerInfo expected = Server("10.0.0.8");
                Assert.That(recovery.Start(expected, 0.5f, "test", CancellationToken.None), Is.True);

                Task finished = await Task.WhenAny(attempted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.That(finished, Is.EqualTo(attempted.Task), "Recovery did not attempt within the test timeout.");
                Assert.That(connectedTarget, Is.SameAs(expected));
            }
        });

        [UnityTest]
        public IEnumerator NewConnectionIntent_CancelsOldEndpointBeforeAttempt() => UnityTaskTest.Run(async () =>
        {
            int connectCalls = 0;
            using (var recovery = CreateRecovery(
                       (target, token) =>
                       {
                           connectCalls++;
                           return Task.FromResult(ConnectionResult.Ok());
                       },
                       _ => { }))
            {
                Assert.That(recovery.Start(Server("10.0.0.8"), 0.5f, "test", CancellationToken.None), Is.True);
                recovery.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(0.6));
                Assert.That(connectCalls, Is.Zero);
            }
        });

        [UnityTest]
        public IEnumerator NewConnectionIntent_CancelsInFlightConnect() => UnityTaskTest.Run(async () =>
        {
            var started = new TaskCompletionSource<bool>();
            var canceled = new TaskCompletionSource<bool>();
            using (var recovery = CreateRecovery(
                       async (target, token) =>
                       {
                           started.TrySetResult(true);
                           using (token.Register(() => canceled.TrySetResult(true)))
                               await Task.Delay(TimeSpan.FromSeconds(10), token);
                           return ConnectionResult.Ok();
                       },
                       _ => { }))
            {
                Assert.That(recovery.Start(Server("10.0.0.8"), 0.5f, "test", CancellationToken.None), Is.True);
                Assert.That(await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(2))),
                    Is.EqualTo(started.Task));

                recovery.Cancel();
                Assert.That(await Task.WhenAny(canceled.Task, Task.Delay(TimeSpan.FromSeconds(2))),
                    Is.EqualTo(canceled.Task));
            }
        });

        private static PulletActiveServerRecovery CreateRecovery(
            Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> connect,
            Action<int> onAttempt)
        {
            return new PulletActiveServerRecovery(
                () => false,
                connect,
                () => { },
                () => true,
                onAttempt,
                _ => { },
                _ => { });
        }

        private static PulletServerInfo Server(string host)
        {
            return new PulletServerInfo
            {
                instanceId = "BUS-01",
                serverName = "BUS-01",
                host = host,
                tcpPort = 19001,
                udpPort = 19000,
                webPort = 7779
            };
        }
    }
}
