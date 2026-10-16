using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Tests
{
    public sealed class ClientDispatchQueueTests
    {
        private NetClientHost _host;
        private readonly List<ClientDispatchQueue> _queues = new List<ClientDispatchQueue>();

        [SetUp] public void SetUp() { _host = new NetClientHost(); }
        [UnityTearDown] public IEnumerator TearDown() => UnityTaskTest.Run(async () =>
        {
            foreach (var queue in _queues) await queue.CloseAsync();
            _queues.Clear();
            Assert.That((await _host.CloseAllAsync()).IsSuccess, Is.True);
        });

        private ClientDispatchQueue Queue(ClientDispatchLimits limits = null, Func<ReceivedPayload, OwnedPayload> copy = null)
        {
            var queue = new ClientDispatchQueue(limits ?? new ClientDispatchLimits(), copy);
            _queues.Add(queue);
            return queue;
        }
        private ClientDispatchQueue.Source Source(ClientDispatchQueue queue, string key)
        {
            var source = queue.Register(_host.Create(key, new NetClientOptions()));
            queue.BeginSession(source);
            return source;
        }
        private static ReceivedPayload Payload(int bytes = 1, ChannelType mode = ChannelType.Unreliable, byte id = 4)
            => new ReceivedPayload(new byte[bytes], TransportKind.Udp, mode, id);
        private static void Balanced(ClientDispatchQueue queue)
        {
            var snapshot = queue.Snapshot();
            Assert.That(snapshot.Payloads, Is.Zero);
            Assert.That(snapshot.Bytes, Is.Zero);
            Assert.That(snapshot.Preparing, Is.Zero);
            Assert.That(snapshot.Consuming, Is.Zero);
            Assert.That(snapshot.Accepted, Is.EqualTo(snapshot.Released));
        }

        private static int Drain(ClientDispatchQueue queue, Action<HostedClient, long, ReceivedPayload> callback)
        {
            // The production queue deliberately isolates diagnostic exceptions.
            // Capture then assert outside Drain so an assertion cannot be swallowed.
            var errors = new List<Exception>();
            int count = queue.Drain(callback, (_, error) => errors.Add(error));
            Assert.That(errors, Is.Empty);
            return count;
        }

        [Test]
        public void FrozenLimitsRejectBadConfigurationAndDoNotTrackLaterMutation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClientDispatchQueue(new ClientDispatchLimits { MaximumBytes = 0 }));
            var limits = new ClientDispatchLimits { MaximumPayloads = 1 };
            var queue = Queue(limits); var source = Source(queue, "a");
            limits.MaximumPayloads = 99;
            Assert.That(queue.Enqueue(source, Payload()), Is.EqualTo(ClientPayloadAdmission.Accepted));
            Assert.That(queue.Enqueue(source, Payload()), Is.EqualTo(ClientPayloadAdmission.Dropped));
        }

        [Test]
        public void SameHandleCannotRegisterTwiceAndForeignQueueCannotConsumeIt()
        {
            var first = Queue(); var second = Queue(); var source = Source(first, "a");
            Assert.Throws<InvalidOperationException>(() => first.Register(source.Client));
            Assert.Throws<ArgumentException>(() => second.Enqueue(source, Payload()));
            Assert.Throws<ArgumentException>(() => second.BeginSession(source));
            Balanced(second);
        }

        [Test]
        public void SourceCapacityIsEnforcedAndRetirementReleasesTheSlot()
        {
            var queue = Queue(new ClientDispatchLimits { MaximumSources = 1 });
            var a = Source(queue, "a"); var b = _host.Create("b", new NetClientOptions());
            Assert.Throws<InvalidOperationException>(() => queue.Register(b));
            Assert.That(queue.RetireAsync(a).IsCompleted, Is.True);
            Assert.DoesNotThrow(() => queue.Register(b));
        }

        [Test]
        public void GlobalBytesAndPerSourceBytesAreReservedBeforeCopy()
        {
            int copies = 0;
            var queue = Queue(new ClientDispatchLimits { MaximumBytes = 6, BytesPerSource = 4 }, p => { copies++; return p.ToOwned(); });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            Assert.That(queue.Enqueue(a, Payload(4)), Is.EqualTo(ClientPayloadAdmission.Accepted));
            Assert.That(queue.Enqueue(a, Payload()), Is.EqualTo(ClientPayloadAdmission.Dropped));
            Assert.That(queue.Enqueue(b, Payload(3)), Is.EqualTo(ClientPayloadAdmission.Dropped));
            Assert.That(queue.Enqueue(b, Payload(2)), Is.EqualTo(ClientPayloadAdmission.Accepted));
            Assert.That(copies, Is.EqualTo(2));
            Assert.That(queue.Snapshot().Bytes, Is.EqualTo(6));
        }

        [Test]
        public void UnreliableOverflowNeverEvictsAnotherSourcesPayload()
        {
            var queue = Queue(new ClientDispatchLimits { MaximumPayloads = 1 });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            queue.Enqueue(a, Payload());
            Assert.That(queue.Enqueue(b, Payload()), Is.EqualTo(ClientPayloadAdmission.Dropped));
            var received = new List<HostedClient>();
            Drain(queue, (client, generation, payload) => received.Add(client));
            Assert.That(received, Is.EqualTo(new[] { a.Client }));
            Balanced(queue);
        }

        [TestCase(ChannelType.ReliableOrdered)]
        [TestCase(ChannelType.ReliableUnordered)]
        [TestCase(ChannelType.ReliableSequenced)]
        public void ReliableOverflowLatchesSourceFailureWithoutFaultingOtherSources(ChannelType mode)
        {
            var queue = Queue(new ClientDispatchLimits { PayloadsPerSource = 1 });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            queue.Enqueue(a, Payload(mode: mode));
            Assert.That(queue.Enqueue(a, Payload(mode: mode)), Is.EqualTo(ClientPayloadAdmission.SourceFaulted));
            Assert.That(queue.Failure(a, out _), Is.EqualTo(ClientDispatchFailure.ReliableOverflow));
            Assert.Throws<InvalidOperationException>(() => queue.BeginSession(a));
            Assert.That(queue.Enqueue(a, Payload()), Is.EqualTo(ClientPayloadAdmission.Closed));
            Assert.That(queue.Enqueue(b, Payload()), Is.EqualTo(ClientPayloadAdmission.Accepted));
            Drain(queue, (client, generation, payload) => Assert.That(client, Is.SameAs(b.Client)));
            Balanced(queue);
        }

        [Test]
        public void OversizedItemCannotStrandAFrameAndIsRejectedBeforeCopy()
        {
            int copies = 0;
            var queue = Queue(new ClientDispatchLimits { BytesPerFrame = 4 }, p => { copies++; return p.ToOwned(); });
            var source = Source(queue, "a");
            Assert.That(queue.Enqueue(source, Payload(5)), Is.EqualTo(ClientPayloadAdmission.Dropped));
            Assert.That(queue.Enqueue(source, Payload(5, ChannelType.ReliableOrdered)), Is.EqualTo(ClientPayloadAdmission.SourceFaulted));
            Assert.That(copies, Is.Zero);
            Balanced(queue);
        }

        [Test]
        public void RoundRobinRetainsItsCursorAcrossFrameBoundaries()
        {
            var queue = Queue(new ClientDispatchLimits { CallbacksPerFrame = 1 });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            for (int i = 0; i < 3; i++) queue.Enqueue(a, Payload());
            queue.Enqueue(b, Payload());
            var delivered = new List<HostedClient>();
            Drain(queue, (client, generation, payload) => delivered.Add(client));
            Drain(queue, (client, generation, payload) => delivered.Add(client));
            Assert.That(delivered, Is.EqualTo(new[] { a.Client, b.Client }));
        }

        [Test]
        public void FrameByteBudgetSkipsAnItemThatDoesNotFitThisFrameWithoutDiscardingIt()
        {
            var queue = Queue(new ClientDispatchLimits { BytesPerFrame = 5 });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            queue.Enqueue(a, Payload(4)); queue.Enqueue(b, Payload(2));
            Assert.That(queue.Drain((_, __, ___) => { }, null), Is.EqualTo(1));
            Assert.That(queue.Snapshot().Bytes, Is.EqualTo(2));
            Assert.That(queue.Drain((_, __, ___) => { }, null), Is.EqualTo(1));
            Balanced(queue);
        }

        [Test]
        public void DeliveryBorrowsOwnedCopyAndPreservesAllMetadataUntilCallbackExit()
        {
            OwnedPayload owned = null;
            var queue = Queue(copy: p => owned = p.ToOwned()); var source = Source(queue, "a");
            byte[] bytes = { 1, 42 };
            queue.Enqueue(source, new ReceivedPayload(bytes, TransportKind.WebSocket, ChannelType.ReliableOrdered, 7));
            bytes[0] = 99;
            Drain(queue, (client, generation, payload) =>
            {
                Assert.That(owned.IsDisposed, Is.False);
                Assert.That(payload.Memory.ToArray(), Is.EqualTo(new byte[] { 1, 42 }));
                Assert.That(payload.ChannelId, Is.EqualTo((byte)7));
                Assert.That(payload.TransportType, Is.EqualTo(TransportKind.WebSocket));
                Assert.That(payload.ChannelType, Is.EqualTo(ChannelType.ReliableOrdered));
                Assert.That(queue.Snapshot().Consuming, Is.EqualTo(1));
                Assert.That(queue.Snapshot().Bytes, Is.EqualTo(2));
            });
            Assert.That(owned.IsDisposed, Is.True);
            Balanced(queue);
        }

        [Test]
        public void CallbackAndDiagnosticExceptionsDoNotSkipNextSourceOrRelease()
        {
            var owned = new List<OwnedPayload>();
            var queue = Queue(copy: p => { var copy = p.ToOwned(); owned.Add(copy); return copy; });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            queue.Enqueue(a, Payload()); queue.Enqueue(b, Payload());
            int delivered = 0;
            queue.Drain((client, generation, payload) => { delivered++; if (client == a.Client) throw new Exception("callback"); },
                (_, error) => throw new Exception("diagnostic"));
            Assert.That(delivered, Is.EqualTo(2));
            Assert.That(owned.TrueForAll(p => p.IsDisposed), Is.True);
            Balanced(queue);
        }

        [Test]
        public void InvalidateOnlyClearsItsSourceAndNewSessionHasDistinctGeneration()
        {
            var queue = Queue(); var a = Source(queue, "a"); var b = Source(queue, "b");
            long previous = a.Generation;
            queue.Enqueue(a, Payload()); queue.Enqueue(b, Payload());
            queue.Invalidate(a);
            Assert.That(queue.BeginSession(a), Is.GreaterThan(previous));
            var received = new List<HostedClient>();
            Drain(queue, (client, generation, payload) => received.Add(client));
            Assert.That(received, Is.EqualTo(new[] { b.Client }));
            Balanced(queue);
        }

        [UnityTest]
        public IEnumerator ReusedNameKeepsAnExactNonReusableSourceHandle() => UnityTaskTest.Run(async () =>
        {
            var queue = Queue(); var a = Source(queue, "same");
            await queue.RetireAsync(a);
            Assert.That((await _host.RemoveAsync(a.Client)).IsSuccess, Is.True);
            var b = Source(queue, "same");
            Assert.That(a.Client, Is.Not.SameAs(b.Client));
            Assert.That(queue.Enqueue(a, Payload()), Is.EqualTo(ClientPayloadAdmission.Closed));
            Assert.That(queue.Enqueue(b, Payload()), Is.EqualTo(ClientPayloadAdmission.Accepted));
            Drain(queue, (client, generation, payload) => Assert.That(client, Is.SameAs(b.Client)));
            Balanced(queue);
        });

        [Test]
        public void CopyFailureRollsBackAndRetainsTheOriginalSourceFailure()
        {
            var expected = new InvalidOperationException("copy");
            var queue = Queue(copy: p => throw expected); var source = Source(queue, "a");
            Assert.That(queue.Enqueue(source, Payload(mode: ChannelType.ReliableOrdered)), Is.EqualTo(ClientPayloadAdmission.CopyFailed));
            Assert.That(queue.Failure(source, out var error), Is.EqualTo(ClientDispatchFailure.CopyFailure));
            Assert.That(error, Is.SameAs(expected));
            Assert.That(queue.Enqueue(source, Payload()), Is.EqualTo(ClientPayloadAdmission.Closed));
            Assert.That(queue.Failure(source, out error), Is.EqualTo(ClientDispatchFailure.CopyFailure));
            Balanced(queue);
        }

        [Test]
        public void ConsumingPayloadStillOwnsItsCountAndByteBudget()
        {
            var queue = Queue(new ClientDispatchLimits { MaximumPayloads = 1, MaximumBytes = 3 });
            var a = Source(queue, "a"); var b = Source(queue, "b");
            queue.Enqueue(a, Payload(3));
            Drain(queue, (_, __, ___) =>
            {
                Assert.That(queue.Enqueue(b, Payload()), Is.EqualTo(ClientPayloadAdmission.Dropped));
                Assert.That(queue.Snapshot().Payloads, Is.EqualTo(1));
                Assert.That(queue.Snapshot().Bytes, Is.EqualTo(3));
            });
            Assert.That(queue.Enqueue(b, Payload(3)), Is.EqualTo(ClientPayloadAdmission.Accepted));
            queue.CloseAsync(); Balanced(queue);
        }

        [Test]
        public void InvalidCopyMetadataReleasesOwnedMemoryAndFaultsReliableSource()
        {
            OwnedPayload copied = null;
            var queue = Queue(copy: p => copied = new ReceivedPayload(p.Memory, p.TransportType, p.ChannelType, 9).ToOwned());
            var source = Source(queue, "a");
            Assert.That(queue.Enqueue(source, Payload(mode: ChannelType.ReliableOrdered)), Is.EqualTo(ClientPayloadAdmission.CopyFailed));
            Assert.That(copied.IsDisposed, Is.True);
            Assert.That(queue.Failure(source, out _), Is.EqualTo(ClientDispatchFailure.CopyFailure));
            Balanced(queue);
        }

        [Test]
        public void UnreliableCopyFailureDoesNotPreventFollowingPayloads()
        {
            int copies = 0;
            var queue = Queue(copy: p => { if (++copies == 1) throw new Exception("copy"); return p.ToOwned(); });
            var source = Source(queue, "a");
            Assert.That(queue.Enqueue(source, Payload()), Is.EqualTo(ClientPayloadAdmission.CopyFailed));
            Assert.That(queue.Enqueue(source, Payload()), Is.EqualTo(ClientPayloadAdmission.Accepted));
            Assert.That(queue.Failure(source, out _), Is.EqualTo(ClientDispatchFailure.None));
            queue.Drain((_, __, ___) => { }, null); Balanced(queue);
        }

        [Test]
        public void CallbackMayRequestCloseButItCompletesOnlyAfterItsBorrowEnds()
        {
            var queue = Queue(); var source = Source(queue, "a"); queue.Enqueue(source, Payload());
            Task close = null;
            Drain(queue, (_, __, ___) =>
            {
                close = queue.CloseAsync();
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(queue.CloseAsync(), Is.SameAs(close));
                Assert.Throws<InvalidOperationException>(() => queue.Drain((a, b, c) => { }, null));
            });
            Assert.That(close.IsCompleted, Is.True);
            Assert.Throws<ObjectDisposedException>(() => queue.Register(source.Client));
            Balanced(queue);
        }

        [UnityTest]
        public IEnumerator CloseJoinsPreparationAndReturnsBudgetAfterTheOwnedCopyIsDisposed() => UnityTaskTest.Run(async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OwnedPayload owned = null;
            var queue = Queue(copy: p => { entered.TrySetResult(true); release.Wait(); return owned = p.ToOwned(); });
            var source = Source(queue, "a");
            var producer = Task.Run(() => queue.Enqueue(source, Payload(3)));
            Task close = null;
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(2000)), Is.SameAs(entered.Task));
                Assert.That(queue.Snapshot().Preparing, Is.EqualTo(1));
                Assert.That(queue.Snapshot().Bytes, Is.EqualTo(3));
                close = queue.CloseAsync();
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(queue.Enqueue(source, Payload()), Is.EqualTo(ClientPayloadAdmission.Closed));
            }
            finally { release.Set(); await producer; }
            Assert.That(producer.Result, Is.EqualTo(ClientPayloadAdmission.Invalidated));
            await close;
            Assert.That(owned.IsDisposed, Is.True); Balanced(queue);
        });

        [UnityTest]
        public IEnumerator NewSessionCannotAdoptAnOldCopyThatFinishesLate() => UnityTaskTest.Run(async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OwnedPayload owned = null;
            var queue = Queue(copy: p => { entered.TrySetResult(true); release.Wait(); return owned = p.ToOwned(); });
            var source = Source(queue, "a");
            var producer = Task.Run(() => queue.Enqueue(source, Payload()));
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(2000)), Is.SameAs(entered.Task));
                queue.Invalidate(source); queue.BeginSession(source);
            }
            finally { release.Set(); await producer; }
            Assert.That(producer.Result, Is.EqualTo(ClientPayloadAdmission.Invalidated));
            Assert.That(owned.IsDisposed, Is.True); Balanced(queue);
        });

        [UnityTest]
        public IEnumerator RetireJoinsAnEnteredCallbackAndKeepsTheSourceSlotReserved() => UnityTaskTest.Run(async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = Queue(new ClientDispatchLimits { MaximumSources = 1 });
            var source = Source(queue, "a"); queue.Enqueue(source, Payload(3));
            var consumer = Task.Run(() => queue.Drain((_, __, ___) => { entered.TrySetResult(true); release.Wait(); }, null));
            Task retire = null;
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(2000)), Is.SameAs(entered.Task));
                retire = queue.RetireAsync(source);
                Assert.That(retire.IsCompleted, Is.False);
                Assert.That(queue.Snapshot().Consuming, Is.EqualTo(1));
                Assert.That(queue.Snapshot().Bytes, Is.EqualTo(3));
                var b = _host.Create("b", new NetClientOptions());
                Assert.Throws<InvalidOperationException>(() => queue.Register(b));
            }
            finally { release.Set(); await consumer; }
            await retire;
            Assert.That(queue.Snapshot().Sources, Is.Zero); Balanced(queue);
        });

        [UnityTest]
        public IEnumerator ConcurrentProducersCannotOverdrawGlobalBudget() => UnityTaskTest.Run(async () =>
        {
            var queue = Queue(new ClientDispatchLimits { MaximumBytes = 10 }); var source = Source(queue, "a");
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var producers = new Task<ClientPayloadAdmission>[40];
            for (int i = 0; i < producers.Length; i++)
                producers[i] = Task.Run(async () => { await start.Task; return queue.Enqueue(source, Payload()); });
            start.SetResult(true);
            var results = await Task.WhenAll(producers);
            int accepted = 0; foreach (var result in results) if (result == ClientPayloadAdmission.Accepted) accepted++;
            Assert.That(accepted, Is.EqualTo(10));
            Assert.That(queue.Snapshot().Bytes, Is.EqualTo(10));
            Assert.That(queue.Snapshot().Dropped, Is.EqualTo(30));
            await queue.CloseAsync(); Balanced(queue);
        });
    }
}
