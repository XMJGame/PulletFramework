using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletClientRuntimeTests
    {
        private readonly List<PulletClientRuntime> _runtimes = new List<PulletClientRuntime>();
        private PulletClientRuntime Runtime(PulletClientRuntimeOptions options = null, Action<HostedClient> beforeRemove = null)
        {
            var runtime = new PulletClientRuntime(options, beforeRemove); _runtimes.Add(runtime); return runtime;
        }
        private static HostedClient Source(PulletClientRuntime runtime, string key)
            => runtime.Create(key, new NetClientOptions { KeepAlive = new KeepAliveOptions { EnableKeepAlive = false }, Reconnect = new ReconnectOptions { EnableReconnect = false } });
        private static void Open(PulletClientRuntime runtime, HostedClient source)
            => runtime.OnState(source, new ConnectionStateChangedEvent(ConnectionState.Handshaking, ConnectionState.Connected));
        private static ReceivedPayload Payload(byte value = 1)
            => new ReceivedPayload(new byte[] { value }, TransportKind.Udp, ChannelType.ReliableOrdered, 7);
        [UnityTearDown] public IEnumerator TearDown() => UnityTaskTest.Run(async () =>
        {
            foreach (var runtime in _runtimes) await runtime.CloseAsync();
            _runtimes.Clear();
        });

        [Test]
        public void InvalidLimitsAreRejectedBeforeCreatingClients()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PulletClientRuntime(new PulletClientRuntimeOptions { LifecycleEventsPerClient = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PulletClientRuntime(new PulletClientRuntimeOptions { PayloadBytesPerFrame = 0 }));
        }
        [Test]
        public void CreationIsBoundedDoesNotConnectAndInvalidOptionsRollBack()
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { MaximumClients = 1 });
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Create("bad", new NetClientOptions { ProtocolVersion = 2 }));
            Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
            var a = Source(runtime, "a");
            Assert.That(a.Client.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(runtime.TryGetClient("a", out var found), Is.True); Assert.That(found, Is.SameAs(a));
            Assert.Throws<InvalidOperationException>(() => Source(runtime, "b"));
            Assert.Throws<InvalidOperationException>(() => Source(runtime, "a"));
        }
        [Test]
        public void LifecyclePrecedesPayloadEvenWithOneEventPerFrame()
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { LifecycleCallbacksPerFrame = 1 }); var a = Source(runtime, "a");
            Open(runtime, a); runtime.Record(a, PulletClientLifecycleKind.Connected, new ConnectedEvent(1, 2, ConnectionMode.UdpOnly));
            runtime.Receive(a, Payload());
            var order = new List<string>();
            runtime.LifecycleChanged += e => order.Add(e.Kind.ToString());
            runtime.PayloadReceived += e => order.Add("payload");
            runtime.Update(); Assert.That(order, Is.EqualTo(new[] { "StateChanged" }));
            runtime.Update(); Assert.That(order, Is.EqualTo(new[] { "StateChanged", "Connected", "payload" }));
        }
        [Test]
        public void PayloadHasExactSourceGenerationAndChannelMetadata()
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a);
            runtime.Receive(a, Payload(42));
            var received = new List<PulletClientPayloadEvent>();
            runtime.PayloadReceived += e => received.Add(new PulletClientPayloadEvent(e.Source, e.DispatchGeneration,
                new ReceivedPayload(e.Payload.Memory.ToArray(), e.Payload.TransportType, e.Payload.ChannelType, e.Payload.ChannelId)));
            runtime.Update();
            Assert.That(received.Count, Is.EqualTo(1)); Assert.That(received[0].Source, Is.SameAs(a));
            Assert.That(received[0].DispatchGeneration, Is.GreaterThan(0));
            Assert.That(received[0].Payload.ChannelId, Is.EqualTo((byte)7));
            Assert.That(received[0].Payload.Memory.Span[0], Is.EqualTo(42));
            Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero);
        }
        [Test]
        public void SingleSourceInvalidationCannotClearTheOtherSource()
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); var b = Source(runtime, "b");
            Open(runtime, a); Open(runtime, b); runtime.Receive(a, Payload()); runtime.Receive(b, Payload());
            runtime.OnState(a, new ConnectionStateChangedEvent(ConnectionState.Connected, ConnectionState.Disconnected));
            var received = new List<HostedClient>(); runtime.PayloadReceived += e => received.Add(e.Source);
            runtime.Update(); Assert.That(received, Is.EqualTo(new[] { b }));
        }
        [Test]
        public void ReconnectingWithoutHandshakingPreservesGenerationAndQueuedPayload()
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a);
            var generations = new List<long>(); runtime.PayloadReceived += e => generations.Add(e.DispatchGeneration);
            runtime.Receive(a, Payload()); runtime.Update();
            runtime.Receive(a, Payload());
            runtime.OnState(a, new ConnectionStateChangedEvent(ConnectionState.Connected, ConnectionState.Reconnecting));
            runtime.OnState(a, new ConnectionStateChangedEvent(ConnectionState.Reconnecting, ConnectionState.Connected));
            runtime.Update(); Assert.That(generations.Count, Is.EqualTo(2)); Assert.That(generations[1], Is.EqualTo(generations[0]));
        }
        [Test]
        public void NewHandshakeInvalidatesOldPayloadAndAdvancesDispatchGeneration()
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a);
            var generations = new List<long>(); runtime.PayloadReceived += e => generations.Add(e.DispatchGeneration);
            runtime.Receive(a, Payload()); runtime.Update();
            runtime.Receive(a, Payload());
            runtime.OnState(a, new ConnectionStateChangedEvent(ConnectionState.Connected, ConnectionState.Reconnecting));
            runtime.OnState(a, new ConnectionStateChangedEvent(ConnectionState.Reconnecting, ConnectionState.Handshaking));
            Open(runtime, a); runtime.Receive(a, Payload()); runtime.Update();
            Assert.That(generations.Count, Is.EqualTo(2)); Assert.That(generations[1], Is.GreaterThan(generations[0]));
        }
        [Test]
        public void MulticastSubscriberFailureDoesNotSkipOthersOrLeakPayload()
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a); runtime.Receive(a, Payload());
            int delivered = 0; int errors = 0;
            Action<PulletClientPayloadEvent> callbacks = e => throw new Exception("first"); callbacks += e => delivered++;
            runtime.PayloadReceived += callbacks;
            runtime.CallbackError += e => { errors++; throw new Exception("diagnostic"); };
            runtime.Update();
            Assert.That(delivered, Is.EqualTo(1)); Assert.That(errors, Is.EqualTo(1));
            Assert.That(runtime.GetSnapshot().DiagnosticHandlerFailures, Is.EqualTo(1)); Assert.That(runtime.GetSnapshot().Payloads, Is.Zero);
            runtime.PayloadReceived -= callbacks; runtime.Receive(a, Payload()); runtime.Update(); Assert.That(delivered, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator ReliableOverflowCleansOnlyItsSourceWithoutNeedingUpdate() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { PayloadsPerClient = 1 });
            var a = Source(runtime, "a"); var b = Source(runtime, "b"); Open(runtime, a); Open(runtime, b); runtime.Update();
            runtime.Receive(a, Payload()); runtime.Receive(a, Payload());
            Assert.That(runtime.TryGetClient("a", out _), Is.False);
            var removal = runtime.RemoveAsync(a); var result = await removal;
            Assert.That(result.DeliveryFailure.Reason, Is.EqualTo(PulletClientDeliveryFailureReason.ReliableOverflow));
            Assert.That(result.CleanupError, Is.Null); Assert.That(result.IsSuccess, Is.False);
            Assert.That(a.Client.State, Is.EqualTo(ConnectionState.Disposed)); Assert.That(b.Client.State, Is.EqualTo(ConnectionState.Disconnected));
            var faults = new List<PulletClientDeliveryFailure>(); runtime.DeliveryFaulted += e => faults.Add(e);
            runtime.Receive(b, Payload()); int delivered = 0; runtime.PayloadReceived += e => { if (e.Source == b) delivered++; };
            runtime.Update(); runtime.Update(); Assert.That(faults.Count, Is.EqualTo(1));
            Assert.That(faults[0].Source, Is.SameAs(a)); Assert.That(delivered, Is.EqualTo(1)); Assert.That(faults[0].Cleanup, Is.SameAs(removal));
        });
        [UnityTest]
        public IEnumerator LifecycleOverflowHasOneFixedTerminalFactAndDoesNotReopen() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { LifecycleEventsPerClient = 1 }); var a = Source(runtime, "a");
            Open(runtime, a); runtime.Record(a, PulletClientLifecycleKind.Connected, new ConnectedEvent(1, 1, ConnectionMode.UdpOnly));
            Open(runtime, a); runtime.Receive(a, Payload());
            var result = await runtime.RemoveAsync(a);
            Assert.That(result.DeliveryFailure.Reason, Is.EqualTo(PulletClientDeliveryFailureReason.LifecycleOverflow));
            Assert.That(runtime.GetSnapshot().LifecycleEvents, Is.Zero);
            int faults = 0; runtime.DeliveryFaulted += e => faults++; runtime.Update(); runtime.Update(); Assert.That(faults, Is.EqualTo(1));
        });
        [UnityTest]
        public IEnumerator NameReuseAndLateOldFactsCannotTargetTheReplacement() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(); var old = Source(runtime, "same"); Open(runtime, old); runtime.Receive(old, Payload());
            Assert.That((await runtime.RemoveAsync(old)).IsSuccess, Is.True);
            var current = Source(runtime, "same"); Open(runtime, current);
            Open(runtime, old); runtime.Receive(old, Payload()); runtime.Record(old, PulletClientLifecycleKind.Disconnected, new DisconnectedEvent("late", false));
            runtime.Receive(current, Payload()); var received = new List<HostedClient>(); var states = new List<HostedClient>();
            runtime.PayloadReceived += e => received.Add(e.Source); runtime.LifecycleChanged += e => states.Add(e.Source);
            runtime.Update(); Assert.That(received, Is.EqualTo(new[] { current })); Assert.That(states, Is.EqualTo(new[] { current }));
            Assert.Throws<InvalidOperationException>(() => runtime.RemoveAsync(old));
        });
        [UnityTest]
        public IEnumerator CallbackReentrantCloseWaitsForPayloadBorrowAndIsShared() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a); runtime.Receive(a, Payload());
            Task<PulletClientRuntimeCloseResult> close = null; bool completedInside = true; int later = 0;
            runtime.PayloadReceived += e => { close = runtime.CloseAsync(); completedInside = close.IsCompleted; };
            runtime.PayloadReceived += e => later++;
            runtime.Update(); Assert.That(completedInside, Is.False); Assert.That(later, Is.Zero);
            Assert.That(runtime.CloseAsync(), Is.SameAs(close)); Assert.That((await close).IsSuccess, Is.True);
            Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero); Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
        });
        [UnityTest]
        public IEnumerator RemovingInsideLifecycleCallbackJoinsThatEnteredCallback() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a);
            Task<PulletClientCloseResult> removal = null; bool completedInside = true;
            runtime.LifecycleChanged += e => { removal = runtime.RemoveAsync(e.Source); completedInside = removal.IsCompleted; };
            runtime.Update(); Assert.That(completedInside, Is.False); Assert.That((await removal).IsSuccess, Is.True);
            Assert.That(runtime.GetSnapshot().LifecycleEvents, Is.Zero);
        });
        [UnityTest]
        public IEnumerator CancelingOneWaitCannotCancelSharedCleanupOrPermitEarlyNameReuse() => UnityTaskTest.Run(async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = Runtime(beforeRemove: source => { entered.TrySetResult(true); release.Wait(); }); var a = Source(runtime, "a");
            using var cancel = new CancellationTokenSource(); var abandoned = runtime.RemoveAsync(a, cancel.Token);
            var shared = runtime.RemoveAsync(a);
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(2000)), Is.SameAs(entered.Task));
                cancel.Cancel(); bool canceled = false; try { await abandoned; } catch (OperationCanceledException) { canceled = true; }
                Assert.That(canceled, Is.True); Assert.That(shared.IsCompleted, Is.False);
                Assert.Throws<InvalidOperationException>(() => Source(runtime, "a"));
            }
            finally { release.Set(); await shared; }
            Assert.That(shared.Result.IsSuccess, Is.True); Assert.DoesNotThrow(() => Source(runtime, "a"));
        });
        [UnityTest]
        public IEnumerator CloseJoinsAcceptedRemovalAndRejectsNewOperations() => UnityTaskTest.Run(async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = Runtime(beforeRemove: source => { if (source.Key == "a") { entered.TrySetResult(true); release.Wait(); } });
            var a = Source(runtime, "a"); var b = Source(runtime, "b"); var removal = runtime.RemoveAsync(a);
            var close = runtime.CloseAsync();
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(2000)), Is.SameAs(entered.Task)); Assert.That(close.IsCompleted, Is.False);
                Assert.Throws<ObjectDisposedException>(() => Source(runtime, "c"));
                Assert.Throws<ObjectDisposedException>(() => runtime.RemoveAsync(b));
            }
            finally { release.Set(); await removal; await close; }
            Assert.That(close.Result.Clients.Count, Is.EqualTo(2)); Assert.That(close.Result.IsSuccess, Is.True);
        });
        [UnityTest]
        public IEnumerator OneCleanupFailureStillAttemptsEverySdkOwner() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(beforeRemove: source => { if (source.Key == "a") throw new InvalidOperationException("controlled cleanup failure"); });
            var a = Source(runtime, "a"); var b = Source(runtime, "b"); var result = await runtime.CloseAsync();
            Assert.That(result.IsSuccess, Is.False); Assert.That(result.Clients.Count, Is.EqualTo(2));
            Assert.That(a.Client.State, Is.EqualTo(ConnectionState.Disposed)); Assert.That(b.Client.State, Is.EqualTo(ConnectionState.Disposed));
        });
        [UnityTest]
        public IEnumerator PreCancellationDoesNotStartCloseAndForeignSourceCannotRemoveOwnClient() => UnityTaskTest.Run(async () =>
        {
            var a = Runtime(); var b = Runtime(); var source = Source(a, "same"); var other = Source(b, "same");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            bool canceled = false; try { await a.CloseAsync(cancel.Token); } catch (OperationCanceledException) { canceled = true; }
            Assert.That(canceled, Is.True); Assert.That(a.TryGetClient("same", out _), Is.True);
            Assert.Throws<InvalidOperationException>(() => a.RemoveAsync(other)); Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disconnected));
        });
        [UnityTest]
        public IEnumerator CloseReturnsUndeliveredTerminalFailureWithoutAnotherFrame() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { PayloadsPerClient = 1 }); var a = Source(runtime, "a");
            Open(runtime, a); runtime.Update(); runtime.Receive(a, Payload()); runtime.Receive(a, Payload());
            var close = await runtime.CloseAsync();
            Assert.That(close.IsSuccess, Is.False); Assert.That(close.Clients.Count, Is.EqualTo(1));
            Assert.That(close.Clients[0].DeliveryFailure.Reason, Is.EqualTo(PulletClientDeliveryFailureReason.ReliableOverflow));
            Assert.That(close.Clients[0].CleanupError, Is.Null); Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero);
        });
        [UnityTest]
        public IEnumerator FaultNotificationReservesItsSlotUntilDeliveredButNotForever() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { MaximumClients = 1, PayloadsPerClient = 1 }); var a = Source(runtime, "a");
            Open(runtime, a); runtime.Update(); runtime.Receive(a, Payload()); runtime.Receive(a, Payload());
            await runtime.RemoveAsync(a);
            Assert.Throws<InvalidOperationException>(() => Source(runtime, "a"));
            runtime.Update(); Assert.DoesNotThrow(() => Source(runtime, "a"));
        });
        [Test]
        public void AFullLifecycleBudgetIncludesTheCurrentlyConsumingRecord()
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { LifecycleEventsPerClient = 1 }); var a = Source(runtime, "a"); Open(runtime, a);
            runtime.LifecycleChanged += e => runtime.Record(a, PulletClientLifecycleKind.Connected, new ConnectedEvent(1, 1, ConnectionMode.UdpOnly));
            runtime.Update();
            Assert.That(runtime.TryGetClient("a", out _), Is.False);
            Assert.That(runtime.GetSnapshot().LifecycleEvents, Is.Zero);
        }
        [UnityTest]
        public IEnumerator CloseJoinsTerminalNotificationAfterSdkRemovalAlreadyFinished() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(new PulletClientRuntimeOptions { PayloadsPerClient = 1 }); var a = Source(runtime, "a");
            Open(runtime, a); runtime.Update(); runtime.Receive(a, Payload()); runtime.Receive(a, Payload());
            await runtime.RemoveAsync(a);
            Task<PulletClientRuntimeCloseResult> close = null; bool completedInside = true;
            runtime.DeliveryFaulted += e => { close = runtime.CloseAsync(); completedInside = close.IsCompleted; };
            runtime.Update();
            Assert.That(completedInside, Is.False); Assert.That(close, Is.Not.Null);
            var result = await close; Assert.That(result.Clients.Count, Is.EqualTo(1)); Assert.That(result.HostCleanupError, Is.Null);
        });
        [UnityTest]
        public IEnumerator RemoveJoinsPreparingCopyAndRejectsItsLatePublication() => UnityTaskTest.Run(async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var memory = new ControlledMemory(() => { entered.TrySetResult(true); release.Wait(); return new byte[] { 9 }; });
            var runtime = Runtime(); var source = Source(runtime, "a"); Open(runtime, source); runtime.Update();
            var production = Task.Run(() => runtime.Receive(source, new ReceivedPayload(memory.View, TransportKind.Udp, ChannelType.ReliableOrdered, 7)));
            Task<PulletClientCloseResult> removal = null;
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(2000)), Is.SameAs(entered.Task));
                removal = runtime.RemoveAsync(source);
                Assert.That(removal.IsCompleted, Is.False); Assert.That(runtime.GetSnapshot().PreparingPayloads, Is.EqualTo(1));
                Assert.That(runtime.GetSnapshot().PayloadBytes, Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => Source(runtime, "a"));
            }
            finally { release.Set(); await production; if (removal != null) await removal; }
            Assert.That(removal.Result.IsSuccess, Is.True); Assert.That(runtime.GetSnapshot().Payloads, Is.Zero);
            Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero); Assert.DoesNotThrow(() => Source(runtime, "a"));
        });
        [UnityTest]
        public IEnumerator ReliableCopyFailureKeepsOriginalCauseAndSettlesOnlyItsSource() => UnityTaskTest.Run(async () =>
        {
            var error = new InvalidOperationException("controlled copy failure");
            using var memory = new ControlledMemory(() => throw error);
            var runtime = Runtime(); var a = Source(runtime, "a"); var b = Source(runtime, "b");
            Open(runtime, a); Open(runtime, b); runtime.Update();
            runtime.Receive(a, new ReceivedPayload(memory.View, TransportKind.Udp, ChannelType.ReliableOrdered, 7));
            var result = await runtime.RemoveAsync(a);
            Assert.That(result.DeliveryFailure.Reason, Is.EqualTo(PulletClientDeliveryFailureReason.CopyFailure));
            Assert.That(result.DeliveryFailure.Error, Is.SameAs(error)); Assert.That(result.CleanupError, Is.Null);
            Assert.That(runtime.TryGetClient("b", out var current), Is.True); Assert.That(current, Is.SameAs(b));
            Assert.That(runtime.GetSnapshot().PreparingPayloads, Is.Zero); Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero);
        });
        [UnityTest]
        public IEnumerator ConcurrentCreatesAndCloseSettleEveryAcceptedSdkOwner() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(); var created = new List<HostedClient>(); var createdGate = new object();
            for (int i = 0; i < 8; i++) created.Add(Source(runtime, "original-" + i));
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = new Task[17];
            for (int i = 0; i < 16; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    try { var source = Source(runtime, "racing-" + index); lock (createdGate) created.Add(source); }
                    catch (ObjectDisposedException) { /* Close won before this operation was accepted. */ }
                });
            }
            tasks[16] = Task.Run(async () => { await start.Task.ConfigureAwait(false); await runtime.CloseAsync().ConfigureAwait(false); });
            start.SetResult(true); await Task.WhenAll(tasks);
            var close = await runtime.CloseAsync(); Assert.That(close.IsSuccess, Is.True);
            Assert.That(close.Clients.Count, Is.EqualTo(created.Count)); Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
            foreach (var source in created) Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
        });
        [UnityTest]
        public IEnumerator UpdateRejectsForeignThreadAndRecursiveFrame() => UnityTaskTest.Run(async () =>
        {
            var runtime = Runtime(); var a = Source(runtime, "a"); Open(runtime, a);
            bool rejected = await Task.Run(() => { try { runtime.Update(); return false; } catch (InvalidOperationException) { return true; } });
            Assert.That(rejected, Is.True);
            bool recursive = false; runtime.LifecycleChanged += e => { try { runtime.Update(); } catch (InvalidOperationException) { recursive = true; } };
            runtime.Update(); Assert.That(recursive, Is.True);
        });

        private sealed class ControlledMemory : MemoryManager<byte>
        {
            private readonly Func<byte[]> _read;
            internal ControlledMemory(Func<byte[]> read) => _read = read;
            internal ReadOnlyMemory<byte> View => CreateMemory(1);
            public override Span<byte> GetSpan() => _read();
            public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
            public override void Unpin() { }
            protected override void Dispose(bool disposing) { }
        }
    }
}
