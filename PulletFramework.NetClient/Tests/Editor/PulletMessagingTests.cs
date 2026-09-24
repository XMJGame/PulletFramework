using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletFramework.Messaging;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletMessagingTests
    {
        private static readonly PulletMessageProtocolOptions Protocol =
            new PulletMessageProtocolOptions(0x5658);

        [Test]
        public void JsonSerializer_RoundTripsPublicFields()
        {
            var serializer = new JsonMessageSerializer();
            byte[] body = serializer.Serialize(new TestMessage { number = 7, text = "中文" });
            TestMessage value = serializer.Deserialize<TestMessage>(body);

            Assert.That(value.number, Is.EqualTo(7));
            Assert.That(value.text, Is.EqualTo("中文"));
        }

        [Test]
        public void Frame_RejectsWrongBusinessMagic()
        {
            var serializer = new JsonMessageSerializer();
            byte[] payload = PulletMessageFrame.Encode(
                new PulletMessageProtocolOptions(0x1234),
                serializer.Encoding,
                PulletMessageKind.Notification,
                2001,
                0,
                serializer.Serialize(new TestMessage()));

            Assert.That(PulletMessageFrame.TryDecode(Protocol, payload, out _, out string error), Is.False);
            StringAssert.Contains("magic", error.ToLowerInvariant());
        }

        [Test]
        public void Client_AcceptsCustomSerializer()
        {
            var serializer = new TestSerializer();
            var client = CreateClient(serializer);
            TestMessage received = null;
            client.Subscribe<TestMessage>(2001, value => received = value);
            byte[] payload = PulletMessageFrame.Encode(
                Protocol, serializer.Encoding, PulletMessageKind.Notification, 2001, 0, new byte[] { 42 });

            Assert.That(client.HandlePayload(payload, out string error), Is.True, error);
            Assert.That(received, Is.SameAs(serializer.Value));
            client.Dispose();
        }

        [Test]
        public void Subscription_CanBeExplicitlyUnsubscribed()
        {
            var serializer = new JsonMessageSerializer();
            var client = CreateClient(serializer);
            int calls = 0;
            IMessageSubscription subscription = client.Subscribe<TestMessage>(2001, _ => calls++);
            byte[] payload = Notification(serializer, 2001, new TestMessage());

            Assert.That(client.HandlePayload(payload, out _), Is.True);
            Assert.That(client.Unsubscribe(subscription), Is.True);
            Assert.That(subscription.IsSubscribed, Is.False);
            Assert.That(client.HandlePayload(payload, out _), Is.False);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(client.Unsubscribe(subscription), Is.False);
            client.Dispose();
        }

        [Test]
        public void SubscriptionGroup_UnsubscribesAllOwnedSubscriptions()
        {
            var client = CreateClient(new JsonMessageSerializer());
            var group = new MessageSubscriptionGroup();
            IMessageSubscription first = group.Add(client.Subscribe<TestMessage>(2001, _ => { }));
            IMessageSubscription second = group.Add(client.Subscribe<TestMessage>(2002, _ => { }));

            Assert.That(group.Count, Is.EqualTo(2));
            group.UnsubscribeAll();

            Assert.That(group.Count, Is.Zero);
            Assert.That(first.IsSubscribed, Is.False);
            Assert.That(second.IsSubscribed, Is.False);
            group.Dispose();
            client.Dispose();
        }

        [Test]
        public void Subscribe_RejectsDifferentTypesForSameMessageId()
        {
            var client = CreateClient(new JsonMessageSerializer());
            client.Subscribe<TestMessage>(2001, _ => { });

            Assert.Throws<InvalidOperationException>(() =>
                client.Subscribe<OtherMessage>(2001, _ => { }));
            client.Dispose();
        }

        [Test]
        public void HandlerFailure_DoesNotBlockRemainingSubscribers()
        {
            var serializer = new JsonMessageSerializer();
            var client = CreateClient(serializer);
            int calls = 0;
            client.Subscribe<TestMessage>(2001, _ => throw new InvalidOperationException("first failed"));
            client.Subscribe<TestMessage>(2001, _ => calls++);

            bool handled = client.HandlePayload(
                Notification(serializer, 2001, new TestMessage()), out string error);

            Assert.That(handled, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            StringAssert.Contains("first failed", error);
            client.Dispose();
        }

        [Test]
        public void Rpc_CompletesOnlyMatchingResponse()
        {
            var serializer = new JsonMessageSerializer();
            PulletMessageClient client = null;
            client = new PulletMessageClient(Protocol, serializer, (payload, channel, cancellationToken) =>
            {
                Assert.That(PulletMessageFrame.TryDecode(
                    Protocol, payload, out PulletMessageFrame request, out string requestError), Is.True, requestError);
                byte[] response = PulletMessageFrame.Encode(
                    Protocol,
                    serializer.Encoding,
                    PulletMessageKind.Response,
                    1002,
                    request.CorrelationId,
                    serializer.Serialize(new TestMessage { number = 9 }));
                Assert.That(client.HandlePayload(response, out string responseError), Is.True, responseError);
                return new ValueTask<SendResult>(SendResult.Accepted());
            });

            TestMessage result = client.CallAsync<TestMessage, TestMessage>(
                1001, 1002, new TestMessage(), TimeSpan.FromSeconds(1), CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.number, Is.EqualTo(9));
            client.Dispose();
        }

        [Test]
        public void CancelPending_EndsOldSessionRpc()
        {
            var client = CreateClient(new JsonMessageSerializer());
            Task<TestMessage> call = client.CallAsync<TestMessage, TestMessage>(
                1001, 1002, new TestMessage(), TimeSpan.FromSeconds(10), CancellationToken.None);

            client.CancelPending("connection lost");

            Assert.Throws<OperationCanceledException>(() => call.GetAwaiter().GetResult());
            client.Dispose();
        }

        [Test]
        public void Rpc_TimeoutIncludesTimeSpentWaitingForSend()
        {
            var send = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new PulletMessageClient(Protocol, new JsonMessageSerializer(),
                (_, __, ___) => new ValueTask<SendResult>(send.Task));

            Task<TestMessage> call = client.CallAsync<TestMessage, TestMessage>(
                1001, 1002, new TestMessage(), TimeSpan.FromMilliseconds(40));

            Assert.Throws<TimeoutException>(() => call.GetAwaiter().GetResult());
            Assert.That(call.IsCompleted, Is.True);
            client.Dispose();
        }

        [Test]
        public void Rpc_DisconnectCancellationDoesNotWaitForPendingSend()
        {
            var send = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new PulletMessageClient(Protocol, new JsonMessageSerializer(),
                (_, __, ___) => new ValueTask<SendResult>(send.Task));
            Task<TestMessage> call = client.CallAsync<TestMessage, TestMessage>(
                1001, 1002, new TestMessage(), TimeSpan.FromSeconds(10));

            client.CancelPending("connection lost");

            Assert.Throws<OperationCanceledException>(() => call.GetAwaiter().GetResult());
            client.Dispose();
        }

        [Test]
        public void UnknownRpcResponse_IsNotDeliveredToNormalSubscribers()
        {
            var serializer = new JsonMessageSerializer();
            var client = CreateClient(serializer);
            int subscriberCalls = 0;
            client.Subscribe<TestMessage>(1002, _ => subscriberCalls++);
            byte[] response = PulletMessageFrame.Encode(
                Protocol, serializer.Encoding, PulletMessageKind.Response, 1002, 123,
                serializer.Serialize(new TestMessage()));

            Assert.That(client.HandlePayload(response, out string error), Is.False);
            StringAssert.Contains("Unexpected RPC response", error);
            Assert.That(subscriberCalls, Is.Zero);
            client.Dispose();
        }

        private static PulletMessageClient CreateClient(IMessageSerializer serializer)
        {
            return new PulletMessageClient(
                Protocol,
                serializer,
                (payload, channel, cancellationToken) => new ValueTask<SendResult>(SendResult.Accepted()));
        }

        private static byte[] Notification<T>(IMessageSerializer serializer, uint messageId, T value)
        {
            return PulletMessageFrame.Encode(
                Protocol,
                serializer.Encoding,
                PulletMessageKind.Notification,
                messageId,
                0,
                serializer.Serialize(value));
        }

        [Serializable]
        private sealed class TestMessage
        {
            public int number;
            public string text;
        }

        [Serializable]
        private sealed class OtherMessage
        {
            public bool value = true;
        }

        private sealed class TestSerializer : IMessageSerializer
        {
            public readonly TestMessage Value = new TestMessage { number = 42 };
            public PulletMessageEncoding Encoding => PulletMessageEncoding.Json;
            public byte[] Serialize<T>(T value) => new byte[] { 42 };
            public T Deserialize<T>(byte[] body) => (T)(object)Value;
        }
    }
}
