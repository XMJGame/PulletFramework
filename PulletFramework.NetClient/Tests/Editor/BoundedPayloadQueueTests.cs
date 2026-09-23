using NUnit.Framework;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient.Tests
{
    public sealed class BoundedPayloadQueueTests
    {
        [Test]
        public void UnreliableOverflowDropsOldestDroppablePayload()
        {
            using var queue = new BoundedPayloadQueue(2);
            queue.Enqueue(Create(1, ChannelType.Unreliable), null);
            queue.Enqueue(Create(2, ChannelType.ReliableOrdered), null);

            PayloadEnqueueResult result = queue.Enqueue(Create(3, ChannelType.Sequenced), null);

            Assert.That(result, Is.EqualTo(PayloadEnqueueResult.EnqueuedAfterDroppingOldest));
            Assert.That(queue.DroppedCount, Is.EqualTo(1));
            using OwnedPayload first = Dequeue(queue);
            using OwnedPayload second = Dequeue(queue);
            Assert.That(first.Memory.Span[0], Is.EqualTo(2));
            Assert.That(second.Memory.Span[0], Is.EqualTo(3));
        }

        [Test]
        public void ReliableOverflowIsReportedAndIncomingOwnershipIsReleased()
        {
            using var queue = new BoundedPayloadQueue(1);
            queue.Enqueue(Create(1, ChannelType.ReliableOrdered), null);
            OwnedPayload overflow = Create(2, ChannelType.ReliableUnordered);

            PayloadEnqueueResult result = queue.Enqueue(overflow, null);

            Assert.That(result, Is.EqualTo(PayloadEnqueueResult.ReliableOverflow));
            Assert.That(queue.ReliableOverflowCount, Is.EqualTo(1));
            Assert.That(overflow.IsDisposed, Is.True);
        }

        [Test]
        public void DroppableIncomingIsDiscardedWhenQueueContainsOnlyReliablePayloads()
        {
            using var queue = new BoundedPayloadQueue(1);
            queue.Enqueue(Create(1, ChannelType.ReliableOrdered), null);
            OwnedPayload incoming = Create(2, ChannelType.Unreliable);

            PayloadEnqueueResult result = queue.Enqueue(incoming, null);

            Assert.That(result, Is.EqualTo(PayloadEnqueueResult.DroppedIncoming));
            Assert.That(queue.DroppedCount, Is.EqualTo(1));
            Assert.That(incoming.IsDisposed, Is.True);
        }

        [Test]
        public void ClearReleasesEveryQueuedPayload()
        {
            using var queue = new BoundedPayloadQueue(2);
            OwnedPayload first = Create(1, ChannelType.Unreliable);
            OwnedPayload second = Create(2, ChannelType.ReliableOrdered);
            queue.Enqueue(first, null);
            queue.Enqueue(second, null);

            queue.Clear();

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.True);
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void DisposedQueueRejectsAndReleasesLatePayload()
        {
            var queue = new BoundedPayloadQueue(1);
            queue.Dispose();
            OwnedPayload late = Create(1, ChannelType.ReliableOrdered);

            PayloadEnqueueResult result = queue.Enqueue(late, new object());

            Assert.That(result, Is.EqualTo(PayloadEnqueueResult.QueueClosed));
            Assert.That(late.IsDisposed, Is.True);
        }

        private static OwnedPayload Create(byte value, ChannelType channelType)
        {
            return new ReceivedPayload(new[] { value }, TransportKind.Udp, channelType).ToOwned();
        }

        private static OwnedPayload Dequeue(BoundedPayloadQueue queue)
        {
            Assert.That(queue.TryDequeue(out OwnedPayload payload, out _), Is.True);
            return payload;
        }
    }
}
