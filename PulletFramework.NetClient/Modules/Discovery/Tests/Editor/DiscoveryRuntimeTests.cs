using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.Discovery;
using UnityEngine.TestTools;

namespace PulletFramework.Discovery.Tests
{
    public sealed class DiscoveryRuntimeTests
    {
        [Test]
        public void AssemblyBoundaryHasNoClientRuntimeOrUnityEngineDependency()
        {
            foreach (var assembly in new[] { typeof(DiscoveryRuntime).Assembly, typeof(DiscoveryClient).Assembly })
                foreach (var reference in assembly.GetReferencedAssemblies())
                    Assert.That(reference.Name, Does.Not.Match("^(PulletFramework.NetClient|PulletNet.ClientSDK|PulletNet.NetCore|PulletNet.Hosting|PulletNet.Messaging|UnityEngine)"));
        }

        [TestCase(0)] [TestCase(35)]
        public void RejectsInvalidFrameBudget(int limit) => Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryRuntime(limit));

        [UnityTest] public IEnumerator RealReplyHasImmutableSnapshotAndMainThreadDelivery() => Run(async () =>
        {
            using var responder = Passive();
            var runtime = new DiscoveryRuntime();
            int thread = Thread.CurrentThread.ManagedThreadId, callbacks = 0;
            DiscoveryCompletion completion = null;
            runtime.Completed += value => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(thread)); completion = value; callbacks++; };
            var reply = Reply(responder, "first");
            try
            {
                var snapshot = await runtime.DiscoverAsync(Options(responder));
                await reply;
                Assert.That(callbacks, Is.Zero, "IO must not directly invoke application UI");
                Assert.That(snapshot.Servers.Count, Is.EqualTo(1));
                Assert.That(snapshot.Servers[0].Host, Is.EqualTo("127.0.0.1"));
                Assert.That(snapshot.Servers[0].InstanceId, Is.EqualTo("first"));
                Assert.That(typeof(DiscoveredServer).GetProperty("Host").CanWrite, Is.False);
                Assert.Throws<NotSupportedException>(() => ((IList<DiscoveredServer>)snapshot.Servers).Clear());
                runtime.Update();
                Assert.That(callbacks, Is.EqualTo(1)); Assert.That(completion.Result, Is.SameAs(snapshot));
                Assert.That(completion.Error, Is.Null); Assert.That(completion.IsCanceled, Is.False);
            }
            finally { responder.Close(); await reply; await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator NativeResultLimitIsPreserved() => Run(async () =>
        {
            using var responder = Passive(); var runtime = new DiscoveryRuntime();
            var reply = Reply(responder, "first", "second");
            try
            {
                var options = Options(responder); options.MaximumResults = 1;
                var snapshot = await runtime.DiscoverAsync(options); await reply;
                Assert.That(snapshot.Limit, Is.EqualTo(DiscoveryLimit.ResultCount));
                Assert.That(snapshot.Servers.Count, Is.EqualTo(1));
                Assert.That(snapshot.NativeCallbackFailures, Is.Zero);
            }
            finally { responder.Close(); await reply; await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator RoundCannotBeReusedBeforeTerminalDelivery() => Run(async () =>
        {
            using var responder = Passive(); var runtime = new DiscoveryRuntime();
            var ids = new List<long>(); runtime.Completed += value => ids.Add(value.RoundId);
            try
            {
                var first = runtime.DiscoverAsync(Options(responder));
                Assert.Throws<InvalidOperationException>(() => runtime.DiscoverAsync(Options(responder)));
                await first;
                Assert.Throws<InvalidOperationException>(() => runtime.DiscoverAsync(Options(responder)));
                runtime.Update();
                var second = await runtime.DiscoverAsync(Options(responder)); runtime.Update();
                Assert.That(second.RoundId, Is.EqualTo(first.Result.RoundId + 1));
                CollectionAssert.AreEqual(new long[] { 1, 2 }, ids);
            }
            finally { await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator StopJoinsActualReceiveWithoutPumping() => Run(async () =>
        {
            using var responder = Passive(); var runtime = new DiscoveryRuntime();
            var options = Options(responder); options.Timeout = TimeSpan.FromSeconds(3);
            var work = runtime.DiscoverAsync(options);
            try
            {
                await Until(() => runtime.PendingNotices >= 2);
                await runtime.StopAsync();
                try { await work; Assert.Fail("Canceled IO cannot report success"); } catch (OperationCanceledException) { }
                Assert.That(runtime.IsDiscovering, Is.False);
                Assert.That(runtime.PendingNotices, Is.GreaterThan(0));
            }
            finally { await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator CloseIsSharedDiscardsFactsAndJoinsActualIo() => Run(async () =>
        {
            using var responder = Passive(); var runtime = new DiscoveryRuntime(); int calls = 0;
            runtime.Started += _ => calls++; runtime.Completed += _ => calls++;
            var options = Options(responder); options.Timeout = TimeSpan.FromSeconds(3);
            var work = runtime.DiscoverAsync(options);
            var closed = runtime.CloseAsync(); Assert.That(runtime.CloseAsync(), Is.SameAs(closed));
            await closed;
            try { await work; Assert.Fail("Closed discovery unexpectedly succeeded"); } catch (OperationCanceledException) { }
            runtime.Update();
            Assert.That(calls, Is.Zero); Assert.That(runtime.PendingNotices, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => runtime.DiscoverAsync(Options(responder)));
        });

        [UnityTest] public IEnumerator CloseInsideCallbackWaitsForThatCallbackToReturn() => Run(async () =>
        {
            using var responder = Passive(); var runtime = new DiscoveryRuntime(); Task close = null; int completed = 0;
            await runtime.DiscoverAsync(Options(responder)); // IO settled, so callback is the only remaining owner
            runtime.Started += _ => { close = runtime.CloseAsync(); Assert.That(close.IsCompleted, Is.False); };
            runtime.Completed += _ => completed++;
            runtime.Update();
            await close;
            Assert.That(completed, Is.Zero); Assert.That(runtime.PendingNotices, Is.Zero);
        });

        [UnityTest] public IEnumerator CloseCannotSubstituteCancellationForNativeTaskExit() => Run(async () =>
        {
            var native = new TaskCompletionSource<DiscoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken nativeToken = default;
            var runtime = new DiscoveryRuntime((options, attempt, token) => { nativeToken = token; return native.Task; });
            var work = runtime.DiscoverAsync(new DiscoveryOptions());
            var close = runtime.CloseAsync();
            try
            {
                Assert.That(nativeToken.IsCancellationRequested, Is.True);
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(work.IsCompleted, Is.False);
            }
            finally { native.TrySetCanceled(nativeToken); await close; }
            try { await work; Assert.Fail(); } catch (OperationCanceledException) { }
        });

        [UnityTest] public IEnumerator CallbackErrorsDoNotSkipSubscribersOrOwnClose() => Run(async () =>
        {
            using var responder = Passive(); var runtime = new DiscoveryRuntime(); int delivered = 0;
            runtime.Started += _ => throw new InvalidOperationException("controlled subscriber");
            runtime.Started += _ => delivered++;
            runtime.CallbackError += _ => throw new InvalidOperationException("controlled diagnostic");
            try
            {
                await runtime.DiscoverAsync(Options(responder)); runtime.Update();
                Assert.That(delivered, Is.EqualTo(1)); Assert.That(runtime.CallbackFailures, Is.EqualTo(2));
            }
            finally { await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator FrameBudgetAndRecursionGuardPreserveEventOrder() => Run(async () =>
        {
            var expected = new InvalidOperationException("controlled native failure");
            var runtime = new DiscoveryRuntime((options, attempt, token) =>
            {
                attempt(1, 2); attempt(2, 2); return Task.FromException<DiscoveryResult>(expected);
            }, 1);
            var order = new List<int>();
            runtime.Started += _ => { order.Add(0); Assert.Throws<InvalidOperationException>(() => runtime.Update()); };
            runtime.Attempted += (_, number, maximum) => order.Add(number);
            runtime.Completed += value => { Assert.That(value.Error, Is.SameAs(expected)); order.Add(3); };
            try
            {
                try { await runtime.DiscoverAsync(new DiscoveryOptions()); Assert.Fail("Failure not preserved"); }
                catch (InvalidOperationException error) { Assert.That(error, Is.SameAs(expected)); }
                Assert.That(runtime.PendingNotices, Is.EqualTo(4));
                for (int i = 0; i < 4; i++) { runtime.Update(); Assert.That(order.Count, Is.EqualTo(i + 1)); }
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, order);
            }
            finally { await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator NativeFailureRemainsObservableWhenClosingBeforeUpdate() => Run(async () =>
        {
            var expected = new InvalidOperationException("controlled IO failure");
            var runtime = new DiscoveryRuntime((options, attempt, token) => Task.FromException<DiscoveryResult>(expected));
            try { await runtime.DiscoverAsync(new DiscoveryOptions()); Assert.Fail(); }
            catch (InvalidOperationException error) { Assert.That(error, Is.SameAs(expected)); }
            var close = runtime.CloseAsync();
            try { await close; Assert.Fail("Close must not erase native failure"); }
            catch (InvalidOperationException error) { Assert.That(error, Is.SameAs(expected)); }
            Assert.That(runtime.CloseAsync(), Is.SameAs(close)); Assert.That(runtime.PendingNotices, Is.Zero);
        });

        [UnityTest] public IEnumerator ForeignThreadCannotPumpOrStart() => Run(async () =>
        {
            var runtime = new DiscoveryRuntime();
            try
            {
                await Task.Run(() =>
                {
                    Assert.Throws<InvalidOperationException>(() => runtime.Update());
                    Assert.Throws<InvalidOperationException>(() => runtime.DiscoverAsync(new DiscoveryOptions()));
                });
            }
            finally { await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator PreCanceledRequestDoesNotPublishWork() => Run(async () =>
        {
            var runtime = new DiscoveryRuntime(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try
            {
                Assert.Throws<OperationCanceledException>(() => runtime.DiscoverAsync(new DiscoveryOptions(), canceled.Token));
                Assert.That(runtime.PendingNotices, Is.Zero); Assert.That(runtime.IsDiscovering, Is.False);
            }
            finally { await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator IndependentRuntimeCloseCannotCancelAnotherRound() => Run(async () =>
        {
            using var responder = Passive(); var a = new DiscoveryRuntime(); var b = new DiscoveryRuntime();
            var options = Options(responder); options.Timeout = TimeSpan.FromSeconds(1);
            var first = a.DiscoverAsync(options); var second = b.DiscoverAsync(Options(responder));
            try
            {
                await a.CloseAsync();
                try { await first; Assert.Fail(); } catch (OperationCanceledException) { }
                var result = await second;
                Assert.That(result.RoundId, Is.EqualTo(1)); Assert.That(result.Servers.Count, Is.Zero);
            }
            finally { await a.CloseAsync(); await b.CloseAsync(); }
        });

        private static UdpClient Passive() => new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        private static DiscoveryOptions Options(UdpClient server) => new DiscoveryOptions
        {
            Destination = (IPEndPoint)server.Client.LocalEndPoint, Timeout = TimeSpan.FromMilliseconds(100),
            MaximumAttempts = 1, SendInterval = TimeSpan.FromMilliseconds(20), ServiceType = "test"
        };
        private static async Task Reply(UdpClient server, params string[] ids)
        {
            var request = await server.ReceiveAsync();
            var match = Regex.Match(Encoding.UTF8.GetString(request.Buffer), "\"requestId\"\\s*:\\s*\"([^\"]+)\"");
            Assert.That(match.Success, Is.True);
            foreach (string id in ids)
            {
                var json = "{\"magic\":\"PULLET_DISCOVERY\",\"version\":1,\"requestId\":\"" + match.Groups[1].Value
                    + "\",\"instanceId\":\"" + id + "\",\"serviceType\":\"test\",\"host\":\"0.0.0.0\",\"udpPort\":7777}";
                var bytes = Encoding.UTF8.GetBytes(json);
                await server.SendAsync(bytes, bytes.Length, request.RemoteEndPoint);
            }
        }
        private static async Task Until(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition()) { if (clock.Elapsed.TotalSeconds >= 3) throw new TimeoutException("Observation did not arrive"); await Task.Yield(); }
        }
        private static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var clock = Stopwatch.StartNew();
            while (!task.IsCompleted) { Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(10)); yield return null; }
            task.GetAwaiter().GetResult();
        }
    }
}
