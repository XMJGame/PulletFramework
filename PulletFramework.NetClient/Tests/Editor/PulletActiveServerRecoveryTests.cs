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
        public IEnumerator StartDiagnosticFailureCannotLeaveRecoveryRunning() => UnityTaskTest.Run(async () =>
        {
            var cause = new InvalidOperationException("controlled diagnostic failure");
            using (var recovery = new PulletActiveServerRecovery(() => false,
                (target, token) => Task.FromResult(ConnectionResult.Ok()), () => { }, () => true,
                _ => { }, _ => { }, _ => throw cause))
            {
                Assert.That(recovery.Start(Server("127.0.0.1"), 0.5f, "probe", default), Is.True);
                Assert.That(recovery.IsRunning, Is.False, "A failed start diagnostic must settle its published execution owner.");
                var error = await Failure(recovery.Completion);
                Assert.That(((AggregateException)error).Flatten().InnerExceptions, Does.Contain(cause));
                Assert.That(recovery.Start(Server("127.0.0.1"), .5f, "not restartable after fault", default), Is.False);
                Assert.That(await Failure(recovery.StopAsync()), Is.SameAs(error));
            }
        });

        [UnityTest]
        public IEnumerator StopJoinsNonCooperativeConnectAndOnlyCancelsOneWaiter() => UnityTaskTest.Run(async () =>
        {
            var entered = Signal(); var io = new TaskCompletionSource<ConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var recovery = Controlled(async (target, token) => { entered.TrySetResult(true); return await io.Task; });
            Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True); await entered.Task;
            var run = recovery.Completion; using var cancel = new CancellationTokenSource();
            var waiter = recovery.StopAsync(cancel.Token); var shared = recovery.StopAsync();
            try
            {
                cancel.Cancel(); Assert.That(await Failure(waiter), Is.InstanceOf<OperationCanceledException>());
                Assert.That(shared.IsCompleted, Is.False); Assert.That(run.IsCompleted, Is.False); Assert.That(recovery.IsRunning, Is.True);
                Assert.That(recovery.StopAsync(), Is.SameAs(shared));
                Assert.That(recovery.Start(Server("b"), .5f, "cannot replace held owner", default), Is.False);
            }
            finally { io.TrySetResult(ConnectionResult.Ok()); await shared; }
            Assert.That(run.IsCompleted, Is.True); Assert.That(recovery.IsRunning, Is.False);
        });

        [UnityTest]
        public IEnumerator CancelAllowsRestartOnlyAfterTheOriginalConnectExits() => UnityTaskTest.Run(async () =>
        {
            var io = new TaskCompletionSource<ConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            int connects = 0, releases = 0;
            using var recovery = Controlled((target, token) => ++connects == 1 ? io.Task : Task.FromResult(ConnectionResult.Ok()),
                release: () => releases++);
            Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True); var first = recovery.Completion;
            recovery.Cancel(); Assert.That(first.IsCompleted, Is.False);
            Assert.That(recovery.Start(Server("b"), .5f, "held", default), Is.False);
            io.SetResult(ConnectionResult.Fail(ConnectionErrorCode.TransportUnavailable, "late canceled result")); await first;
            Assert.That(releases, Is.Zero); Assert.That(recovery.Start(Server("b"), .5f, "new intent", default), Is.True);
            Assert.That(recovery.Completion, Is.Not.SameAs(first)); await recovery.Completion; await recovery.StopAsync();
            Assert.That(connects, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator ReentrantAttemptStopReturnsSharedJoinWithoutCallingConnect() => UnityTaskTest.Run(async () =>
        {
            PulletActiveServerRecovery recovery = null; Task stop = null; bool completeInside = true; int connects = 0;
            recovery = Controlled((target, token) => { connects++; return Task.FromResult(ConnectionResult.Ok()); },
                attempt: _ => { stop = recovery.StopAsync(); completeInside = stop.IsCompleted; });
            using (recovery)
            {
                Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True);
                Assert.That(completeInside, Is.False); await stop; Assert.That(recovery.StopAsync(), Is.SameAs(stop));
                Assert.That(connects, Is.Zero); Assert.That(recovery.IsRunning, Is.False);
            }
        });

        [UnityTest]
        public IEnumerator StopBeforeDelayExitStillJoinsTheDelayOwner() => UnityTaskTest.Run(async () =>
        {
            var delay = Signal(); int connects = 0;
            using var recovery = Controlled((target, token) => { connects++; return Task.FromResult(ConnectionResult.Ok()); },
                delay: (duration, token) => delay.Task);
            Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True);
            var stop = recovery.StopAsync(); Assert.That(stop.IsCompleted, Is.False);
            delay.SetResult(true); await stop; Assert.That(connects, Is.Zero);
        });

        [UnityTest]
        public IEnumerator ForeignCancellationCannotMasqueradeAsTheOwnersNormalStop() => UnityTaskTest.Run(async () =>
        {
            using var foreign = new CancellationTokenSource(); foreign.Cancel();
            var cause = new OperationCanceledException(foreign.Token); PulletActiveServerRecovery recovery = null;
            recovery = Controlled((target, token) => { recovery.Cancel(); return Task.FromException<ConnectionResult>(cause); });
            using (recovery)
            {
                Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True);
                Assert.That(await Failure(recovery.Completion), Is.SameAs(cause));
                Assert.That(await Failure(recovery.StopAsync()), Is.SameAs(cause));
            }
        });

        [UnityTest]
        public IEnumerator CancellationCallbackFailureCannotSkipJoiningConnect() => UnityTaskTest.Run(async () =>
        {
            var entered = Signal(); var io = Signal(); var cause = new InvalidOperationException("controlled cancel callback");
            using var recovery = Controlled(async (target, token) =>
            {
                using (token.Register(() => throw cause)) { entered.TrySetResult(true); await io.Task; }
                return ConnectionResult.Ok();
            });
            Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True); await entered.Task;
            var stop = recovery.StopAsync();
            try { Assert.That(stop.IsCompleted, Is.False); Assert.That(recovery.Completion.IsCompleted, Is.False); }
            finally { io.TrySetResult(true); }
            var error = await Failure(stop); Assert.That(((AggregateException)error).Flatten().InnerExceptions, Does.Contain(cause));
            Assert.That(recovery.IsRunning, Is.False);
        });

        [UnityTest]
        public IEnumerator PreCanceledStopDoesNotCloseAndDisposeIsPermanentButNotAnExitReceipt() => UnityTaskTest.Run(async () =>
        {
            var io = Signal(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var recovery = Controlled(async (target, token) => { await io.Task; return ConnectionResult.Ok(); });
            Assert.That(await Failure(recovery.StopAsync(canceled.Token)), Is.InstanceOf<OperationCanceledException>());
            Assert.That(recovery.Start(Server("a"), .5f, "test", default), Is.True); recovery.Dispose();
            Assert.That(recovery.IsRunning, Is.True); var stop = recovery.StopAsync(); Assert.That(stop.IsCompleted, Is.False);
            Assert.That(recovery.Start(Server("b"), .5f, "disposed", default), Is.False);
            io.SetResult(true); await recovery.DisposeAsync(); Assert.That(stop.IsCompleted, Is.True);
        });

        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        [UnityTest]
        public IEnumerator ConcurrentStartAndStopCannotReplaceOrOrphanAnAcceptedRun() => UnityTaskTest.Run(async () =>
        {
            var start = Signal(); var io = Signal(); int connects = 0;
            using var recovery = Controlled(async (target, token) =>
            { Interlocked.Increment(ref connects); await io.Task.ConfigureAwait(false); return ConnectionResult.Ok(); });
            var calls = new Task[17];
            for (int i = 0; i < 16; i++) calls[i] = Task.Run(async () =>
            { await start.Task.ConfigureAwait(false); recovery.Start(Server("a"), .5f, "race", default); });
            calls[16] = Task.Run(async () => { await start.Task.ConfigureAwait(false); _ = recovery.StopAsync(); });
            start.SetResult(true);
            try
            {
                await Task.WhenAll(calls); Assert.That(connects, Is.LessThanOrEqualTo(1));
                Assert.That(recovery.Start(Server("b"), .5f, "closed", default), Is.False);
                if (connects != 0) Assert.That(recovery.StopAsync().IsCompleted, Is.False);
            }
            finally { io.TrySetResult(true); await recovery.StopAsync(); }
            Assert.That(recovery.IsRunning, Is.False); Assert.That(recovery.Completion.IsCompleted, Is.True);
        });

        private static PulletActiveServerRecovery Controlled(
            Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> connect, Action<int> attempt = null,
            Action release = null, Func<TimeSpan, CancellationToken, Task> delay = null)
            => new PulletActiveServerRecovery(() => false, connect, release ?? (() => { }), () => true,
                attempt ?? (_ => { }), _ => { }, _ => { }, delay ?? ((duration, token) => Task.CompletedTask));
        private static async Task<Exception> Failure(Task work)
        {
            try { await work; } catch (Exception error) { return error; }
            Assert.Fail("Expected failure was not observed."); return null;
        }

        [UnityTest]
        public IEnumerator Recovery_AttemptsTheExactSelectedEndpoint() => UnityTaskTest.Run(async () =>
        {
            PulletServerInfo connectedTarget = null;
            var attempted = new TaskCompletionSource<bool>();
            await using (var recovery = CreateRecovery(
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
            await using (var recovery = CreateRecovery(
                       (target, token) =>
                       {
                           connectCalls++;
                           return Task.FromResult(ConnectionResult.Ok());
                       },
                       _ => { }))
            {
                Assert.That(recovery.Start(Server("10.0.0.8"), 0.5f, "test", CancellationToken.None), Is.True);
                recovery.Cancel();
                await recovery.Completion;
                Assert.That(connectCalls, Is.Zero);
            }
        });

        [UnityTest]
        public IEnumerator NewConnectionIntent_CancelsInFlightConnect() => UnityTaskTest.Run(async () =>
        {
            var started = new TaskCompletionSource<bool>();
            var canceled = new TaskCompletionSource<bool>();
            await using (var recovery = CreateRecovery(
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
