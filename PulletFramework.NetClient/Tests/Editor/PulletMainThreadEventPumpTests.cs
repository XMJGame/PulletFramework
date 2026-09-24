using System;
using System.Collections.Generic;
using NUnit.Framework;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletMainThreadEventPumpTests
    {
        [Test]
        public void Actions_AreDeliveredInOrderAndFailuresDoNotStopLaterActions()
        {
            using (var pump = new PulletMainThreadEventPump())
            {
                var delivered = new List<int>();
                var errors = new List<Exception>();
                pump.Enqueue(() => delivered.Add(1));
                pump.Enqueue(() => throw new InvalidOperationException("test"));
                pump.Enqueue(() => delivered.Add(2));

                pump.DrainActions(true, errors.Add);

                Assert.That(delivered, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(errors.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void SessionInvalidation_DropsQueuedPayload()
        {
            using (var pump = new PulletMainThreadEventPump())
            {
                pump.Reset(2);
                var client = new object();
                pump.EnqueuePayload(Payload(1), client);
                pump.InvalidateSession();
                int delivered = 0;

                pump.DrainPayloads(2, context => ReferenceEquals(context, client),
                    _ => delivered++, _ => delivered++, ex => Assert.Fail(ex.ToString()));

                Assert.That(delivered, Is.Zero);
            }
        }

        [Test]
        public void SessionInvalidatedDuringMessageDispatch_DoesNotRaiseRawPayloadEvent()
        {
            using (var pump = new PulletMainThreadEventPump())
            {
                pump.Reset(2);
                var client = new object();
                pump.EnqueuePayload(Payload(1), client);
                int messages = 0;
                int rawEvents = 0;

                pump.DrainPayloads(2, context => ReferenceEquals(context, client),
                    _ => { messages++; pump.InvalidateSession(); },
                    _ => rawEvents++, ex => Assert.Fail(ex.ToString()));

                Assert.That(messages, Is.EqualTo(1));
                Assert.That(rawEvents, Is.Zero);
            }
        }

        [Test]
        public void FrameBudget_LimitsPayloadCallbacks()
        {
            using (var pump = new PulletMainThreadEventPump())
            {
                pump.Reset(3);
                var client = new object();
                pump.EnqueuePayload(Payload(1), client);
                pump.EnqueuePayload(Payload(2), client);
                int delivered = 0;

                pump.DrainPayloads(1, context => ReferenceEquals(context, client),
                    _ => { }, _ => delivered++, ex => Assert.Fail(ex.ToString()));
                Assert.That(delivered, Is.EqualTo(1));
                pump.DrainPayloads(1, context => ReferenceEquals(context, client),
                    _ => { }, _ => delivered++, ex => Assert.Fail(ex.ToString()));
                Assert.That(delivered, Is.EqualTo(2));
            }
        }

        [Test]
        public void LatePayloadAfterDispose_IsSafelyDiscarded()
        {
            var pump = new PulletMainThreadEventPump();
            pump.Reset(1);
            pump.Dispose();

            Assert.DoesNotThrow(() => pump.EnqueuePayload(Payload(1), new object()));
        }

        private static ReceivedPayload Payload(byte value)
            => new ReceivedPayload(new[] { value }, TransportKind.Udp, ChannelType.Unreliable);
    }
}
