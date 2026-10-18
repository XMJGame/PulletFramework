using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.Messaging;
using UnityEngine.TestTools;

namespace PulletFramework.Messaging.Tests
{
    public sealed class MessageSessionTests
    {
        private static readonly MessageProtocolOptions Protocol = new MessageProtocolOptions(0x1234, 1, 4);
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
        private static MessageSender Accepted => (_, __) => new ValueTask<MessageSendResult>(MessageSendResult.Accepted);

        [UnityTest] public IEnumerator ExactResponseCompletesOnceAndNeverEntersNotificationRouter() => Run(async () =>
        {
            var codec = new ScalarCodec(); MessageSession session = null; int notifications = 0;
            session = new MessageSession(Protocol, codec, (request, token) =>
            {
                Assert.That(MessageFrameCodec.TryDecode(Protocol, request, out var frame, out _), Is.True);
                var response = Frame(MessageKind.Response, 2, frame.RpcId, 42);
                codec.ExpectedArray = response;
                Assert.That(session.Receive(session.Source, response).Response, Is.EqualTo(ResponseResult.Completed));
                Assert.That(session.Receive(session.Source, response).Response, Is.EqualTo(ResponseResult.UnknownOrLate));
                return new ValueTask<MessageSendResult>(MessageSendResult.Accepted);
            });
            await using (session)
            {
                session.Subscribe<int>(2, _ => notifications++);
                Assert.That(await session.CallAsync<int, int>(1, 2, 7, Deadline), Is.EqualTo(42));
                Assert.That(codec.Decodes, Is.EqualTo(1)); Assert.That(codec.BorrowedOriginalArray, Is.True);
                Assert.That(notifications, Is.Zero); Assert.That(session.RpcSnapshot.PendingCalls, Is.Zero);
            }
        });

        [UnityTest] public IEnumerator SourceAndResponseIdsRejectBeforeDecodingAndDoNotConsumePending() => Run(async () =>
        {
            var codec = new ScalarCodec();
            await using var session = new MessageSession(Protocol, codec, Accepted);
            var call = session.CallAsync<int, int>(1, 2, 0, Deadline);
            var response = Frame(MessageKind.Response, 2, 1, 8);
            Assert.That(session.Receive(MessageSource.Create(), response).Kind, Is.EqualTo(MessageInputKind.SourceMismatch));
            Assert.That(session.Receive(session.Source, Frame(MessageKind.Response, 9, 1, 8)).Response, Is.EqualTo(ResponseResult.MessageIdMismatch));
            Assert.That(codec.Decodes, Is.Zero); Assert.That(session.RpcSnapshot.PendingCalls, Is.EqualTo(1));
            Assert.That(session.Receive(session.Source, response).Response, Is.EqualTo(ResponseResult.Completed));
            Assert.That(await call, Is.EqualTo(8));
        });

        [UnityTest] public IEnumerator NotificationExceptionsStayIsolatedAndPayloadIsOnlyBorrowed() => Run(async () =>
        {
            var codec = new ScalarCodec(); int calls = 0;
            await using var session = new MessageSession(Protocol, codec, Accepted, diagnostic: _ => throw new InvalidOperationException("diagnostic"));
            session.Subscribe<int>(3, _ => throw new InvalidOperationException("handler"));
            session.Subscribe<int>(3, value => { calls++; Assert.That(value, Is.EqualTo(11)); });
            var bytes = Frame(MessageKind.Notification, 3, 0, 11); codec.ExpectedArray = bytes;
            var report = session.Receive(session.Source, bytes).Notification.Value;
            Assert.That(report.Status, Is.EqualTo(NotificationDispatchStatus.Dispatched));
            Assert.That(report.CallbackFailures, Is.EqualTo(1)); Assert.That(calls, Is.EqualTo(1));
            Assert.That(codec.BorrowedOriginalArray, Is.True);
            Assert.That(session.NotificationSnapshot.DiagnosticFailures, Is.EqualTo(1));
            Assert.That(session.NotificationSnapshot.BorrowedBodyBytes, Is.Zero);
            Assert.That(session.IsClosing, Is.False);
        });

        [UnityTest] public IEnumerator CloseInsideNotificationJoinsCallbackAndSkipsLaterSubscribers() => Run(async () =>
        {
            await using var session = new MessageSession(Protocol, new ScalarCodec(), Accepted);
            Task close = null; bool completedInside = true; int later = 0;
            session.Subscribe<int>(3, _ => { close = session.CloseAsync(); completedInside = close.IsCompleted; });
            session.Subscribe<int>(3, _ => later++);
            session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 7));
            Assert.That(completedInside, Is.False); Assert.That(later, Is.Zero);
            Assert.That(close, Is.SameAs(session.Completion)); Assert.That(session.CloseAsync(), Is.SameAs(close));
            await close;
            Assert.That(session.NotificationSnapshot.OccupiedSubscriptions, Is.Zero);
            Assert.That(session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 7)).Kind, Is.EqualTo(MessageInputKind.Closed));
        });

        [UnityTest] public IEnumerator CloseInsideSerializerJoinsPreparationBeforeCompleting() => Run(async () =>
        {
            MessageSession session = null; Task close = null; bool completedInside = true; int sends = 0;
            var codec = new ScalarCodec { BeforeSerialize = () => { close = session.CloseAsync(); completedInside = close.IsCompleted; } };
            session = new MessageSession(Protocol, codec, (_, __) => { sends++; return Accepted(default, default); });
            await using (session)
            {
                var call = session.CallAsync<int, int>(1, 2, 0, Deadline);
                Assert.That(completedInside, Is.False); Assert.That(await Failure(call), Is.InstanceOf<ObjectDisposedException>());
                await close; Assert.That(sends, Is.Zero); Assert.That(session.RpcSnapshot.ActiveCalls, Is.Zero);
                Assert.That(session.RpcSnapshot.ReservedBytes, Is.Zero);
            }
        });

        [UnityTest] public IEnumerator CancellationRetainsBudgetAndCloseJoinsNonCooperativeSend() => Run(async () =>
        {
            var sent = new TaskCompletionSource<MessageSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var limits = new RpcSessionOptions(1, 1, 1, 1, Protocol.MaximumFrameBytes);
            var session = new MessageSession(Protocol, new ScalarCodec(), (_, __) => new ValueTask<MessageSendResult>(sent.Task), limits);
            using var cancel = new CancellationTokenSource();
            try
            {
                var call = session.CallAsync<int, int>(1, 2, 0, Deadline, cancel.Token);
                cancel.Cancel(); Assert.That(await Failure(call), Is.InstanceOf<OperationCanceledException>());
                Assert.That(session.RpcSnapshot.ActiveCalls, Is.EqualTo(1));
                Assert.That(session.RpcSnapshot.ReservedBytes, Is.EqualTo(Protocol.MaximumFrameBytes));
                Assert.Throws<RpcCapacityException>(() => session.CallAsync<int, int>(1, 2, 1, Deadline));
                var close = session.CloseAsync();
                Assert.That(close.IsCompleted, Is.False);
                sent.SetResult(MessageSendResult.Accepted); await close;
                Assert.That(session.RpcSnapshot.ActiveCalls, Is.Zero); Assert.That(session.RpcSnapshot.ReservedBytes, Is.Zero);
            }
            finally { sent.TrySetResult(MessageSendResult.Accepted); await session.CloseAsync(); }
        });

        [UnityTest] public IEnumerator OneCanceledWaiterCannotCancelSharedClose() => Run(async () =>
        {
            var send = new TaskCompletionSource<MessageSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new MessageSession(Protocol, new ScalarCodec(), (_, __) => new ValueTask<MessageSendResult>(send.Task));
            var call = session.CallAsync<int, int>(1, 2, 0, Deadline);
            using var cancel = new CancellationTokenSource();
            try
            {
                var waiter = session.CloseAsync(cancel.Token); var shared = session.CloseAsync();
                cancel.Cancel(); Assert.That(await Failure(waiter), Is.InstanceOf<OperationCanceledException>());
                Assert.That(shared, Is.SameAs(session.Completion)); Assert.That(shared.IsCompleted, Is.False);
                Assert.That(await Failure(call), Is.InstanceOf<ObjectDisposedException>());
                send.SetResult(MessageSendResult.Accepted); await shared;
            }
            finally { send.TrySetResult(MessageSendResult.Accepted); await session.CloseAsync(); }
        });

        [UnityTest] public IEnumerator TimeoutIncludesSendAndDoesNotReleaseUnfinishedIo() => Run(async () =>
        {
            var send = new TaskCompletionSource<MessageSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new MessageSession(Protocol, new ScalarCodec(), (_, __) => new ValueTask<MessageSendResult>(send.Task));
            try
            {
                var call = session.CallAsync<int, int>(1, 2, 0, TimeSpan.FromMilliseconds(40));
                Assert.That(await Failure(call), Is.InstanceOf<TimeoutException>());
                Assert.That(session.RpcSnapshot.SendOperations, Is.EqualTo(1));
                var close = session.CloseAsync(); Assert.That(close.IsCompleted, Is.False);
                send.SetResult(MessageSendResult.Accepted); await close;
            }
            finally { send.TrySetResult(MessageSendResult.Accepted); await session.CloseAsync(); }
        });

        [UnityTest] public IEnumerator PreCanceledCallsAndCloseDoNotStartWork() => Run(async () =>
        {
            var codec = new ScalarCodec(); int sends = 0;
            await using var session = new MessageSession(Protocol, codec, (_, __) => { sends++; return Accepted(default, default); });
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.That(await Failure(session.CallAsync<int, int>(1, 2, 0, Deadline, cancel.Token)), Is.InstanceOf<OperationCanceledException>());
            Assert.That(await Failure(session.CloseAsync(cancel.Token)), Is.InstanceOf<OperationCanceledException>());
            Assert.That(session.IsClosing, Is.False); Assert.That(codec.Encodes, Is.Zero); Assert.That(sends, Is.Zero);
        });

        [UnityTest] public IEnumerator NotificationAndResponseSendsHaveExplicitLocalAdmission() => Run(async () =>
        {
            MessageKind kind = default; ulong id = 0;
            await using var session = new MessageSession(Protocol, new ScalarCodec(), (payload, token) =>
            {
                Assert.That(MessageFrameCodec.TryDecode(Protocol, payload, out var frame, out _), Is.True);
                kind = frame.Kind; id = frame.RpcId;
                return new ValueTask<MessageSendResult>(MessageSendResult.Rejected);
            });
            Assert.That(await session.SendNotificationAsync(3, 4, Deadline), Is.EqualTo(MessageSendResult.Rejected));
            Assert.That(kind, Is.EqualTo(MessageKind.Notification)); Assert.That(id, Is.Zero);
            Assert.That(await session.SendResponseAsync(2, 17, 4, Deadline), Is.EqualTo(MessageSendResult.Rejected));
            Assert.That(kind, Is.EqualTo(MessageKind.Response)); Assert.That(id, Is.EqualTo(17UL));
        });

        [UnityTest] public IEnumerator MalformedInputsAndRequestsNeverReachCodecOrHandlers() => Run(async () =>
        {
            var codec = new ScalarCodec(); await using var session = new MessageSession(Protocol, codec, Accepted); int callbacks = 0;
            session.Subscribe<int>(3, _ => callbacks++);
            Assert.That(session.Receive(session.Source, new byte[1]).FrameError, Is.EqualTo(FrameError.Truncated));
            var invalid = Frame(MessageKind.Notification, 3, 0, 5); invalid[0] ^= 1;
            var report = session.Receive(session.Source, invalid).Notification.Value;
            Assert.That(report.Status, Is.EqualTo(NotificationDispatchStatus.InvalidFrame));
            Assert.That(report.FrameError, Is.EqualTo(FrameError.Magic));
            Assert.That(session.Receive(session.Source, Frame(MessageKind.Request, 1, 1, 5)).Kind, Is.EqualTo(MessageInputKind.UnexpectedKind));
            Assert.That(codec.Decodes, Is.Zero); Assert.That(callbacks, Is.Zero);
        });

        [UnityTest] public IEnumerator ExplicitUnsubscribeStopsDeliveryAndIsIdempotent() => Run(async () =>
        {
            await using var session = new MessageSession(Protocol, new ScalarCodec(), Accepted); int calls = 0;
            var subscription = session.Subscribe<int>(3, _ => calls++);
            session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 1));
            subscription.Unsubscribe(); subscription.Unsubscribe(); subscription.Dispose();
            await subscription.Completion;
            Assert.That(subscription.IsSubscribed, Is.False);
            Assert.That(session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 1)).Notification.Value.Status,
                Is.EqualTo(NotificationDispatchStatus.NoSubscribers));
            Assert.That(calls, Is.EqualTo(1)); Assert.That(session.NotificationSnapshot.OccupiedSubscriptions, Is.Zero);
        });

        [UnityTest] public IEnumerator ApplicationOwnedSubscriptionHandlesRetireOnlyTheirOwnCallbacks() => Run(async () =>
        {
            await using var session = new MessageSession(Protocol, new ScalarCodec(), Accepted); int retired = 0, retained = 0;
            var owned = new[] { session.Subscribe<int>(3, _ => retired++), session.Subscribe<int>(4, _ => retired++) };
            session.Subscribe<int>(3, _ => retained++);
            foreach (var subscription in owned) subscription.Unsubscribe();
            await Task.WhenAll(owned.Select(subscription => subscription.Completion));
            session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 1));
            session.Receive(session.Source, Frame(MessageKind.Notification, 4, 0, 1));
            Assert.That(owned.All(subscription => !subscription.IsSubscribed), Is.True);
            Assert.That(retired, Is.Zero); Assert.That(retained, Is.EqualTo(1));
            Assert.That(session.NotificationSnapshot.OccupiedSubscriptions, Is.EqualTo(1));
        });

        [UnityTest] public IEnumerator OneNotificationIdCannotUseTwoDtoTypes() => Run(async () =>
        {
            var codec = new ScalarCodec(); await using var session = new MessageSession(Protocol, codec, Accepted);
            session.Subscribe<int>(3, _ => { });
            Assert.Throws<InvalidOperationException>(() => session.Subscribe<long>(3, _ => { }));
            Assert.That(session.NotificationSnapshot.OccupiedSubscriptions, Is.EqualTo(1));
            Assert.That(codec.Decodes, Is.Zero);
        });

        [UnityTest] public IEnumerator UnknownResponseNeverCallsNotificationSubscribers() => Run(async () =>
        {
            var codec = new ScalarCodec(); await using var session = new MessageSession(Protocol, codec, Accepted); int notices = 0;
            session.Subscribe<int>(2, _ => notices++);
            Assert.That(session.Receive(session.Source, Frame(MessageKind.Response, 2, 123, 9)).Response, Is.EqualTo(ResponseResult.UnknownOrLate));
            Assert.That(notices, Is.Zero); Assert.That(codec.Decodes, Is.Zero);
            Assert.That(session.RpcSnapshot.PendingCalls, Is.Zero);
        });

        [UnityTest] public IEnumerator UnsubscribeIsCountedUntilAdmittedDispatchActuallyReturns() => Run(async () =>
        {
            await using var session = new MessageSession(Protocol, new ScalarCodec(), Accepted,
                notificationOptions: new NotificationRouterOptions(1));
            PulletNet.Messaging.IMessageSubscription sub = null;
            sub = session.Subscribe<int>(3, _ =>
            {
                sub.Unsubscribe(); Assert.That(sub.Completion.IsCompleted, Is.False);
                Assert.Throws<SubscriptionCapacityException>(() => session.Subscribe<int>(4, __ => { }));
            });
            session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 1));
            await sub.Completion;
            Assert.That(session.NotificationSnapshot.OccupiedSubscriptions, Is.Zero);
            session.Subscribe<int>(4, _ => { });
        });

        [UnityTest] public IEnumerator WrongEncodingDoesNotConsumePendingAndDecodeFailureHasATerminalResult() => Run(async () =>
        {
            var codec = new ScalarCodec(); await using var session = new MessageSession(Protocol, codec, Accepted);
            var call = session.CallAsync<int, int>(1, 2, 0, Deadline);
            var mismatched = MessageFrameCodec.Encode(Protocol, MessageEncoding.Json, MessageKind.Response, 2, 1, new byte[4]);
            Assert.That(session.Receive(session.Source, mismatched).Response, Is.EqualTo(ResponseResult.EncodingMismatch));
            Assert.That(codec.Decodes, Is.Zero); Assert.That(session.RpcSnapshot.PendingCalls, Is.EqualTo(1));
            var malformedBody = MessageFrameCodec.Encode(Protocol, MessageEncoding.Protobuf, MessageKind.Response, 2, 1, ReadOnlyMemory<byte>.Empty);
            Assert.That(session.Receive(session.Source, malformedBody).Response, Is.EqualTo(ResponseResult.DecodeFailed));
            Assert.That(await Failure(call), Is.InstanceOf<ArgumentException>());
            Assert.That(session.RpcSnapshot.PendingCalls, Is.Zero);
        });

        [UnityTest] public IEnumerator OldSourceCannotCompleteAnotherSessionEvenWhenRpcIdsMatch() => Run(async () =>
        {
            var a = new MessageSession(Protocol, new ScalarCodec(), Accepted);
            await using var b = new MessageSession(Protocol, new ScalarCodec(), Accepted);
            var old = a.CallAsync<int, int>(1, 2, 0, Deadline); await a.CloseAsync(); await Failure(old);
            var current = b.CallAsync<int, int>(1, 2, 0, Deadline);
            var response = Frame(MessageKind.Response, 2, 1, 77);
            Assert.That(b.Receive(a.Source, response).Kind, Is.EqualTo(MessageInputKind.SourceMismatch));
            Assert.That(current.IsCompleted, Is.False);
            Assert.That(b.Receive(b.Source, response).Response, Is.EqualTo(ResponseResult.Completed));
            Assert.That(await current, Is.EqualTo(77));
        });

        [UnityTest] public IEnumerator ApiDispatchRequiresItsCreatingThreadButCloseDoesNotNeedUpdate() => Run(async () =>
        {
            await using var session = new MessageSession(Protocol, new ScalarCodec(), Accepted);
            var error = await Task.Run(() =>
            {
                try { session.Receive(session.Source, Frame(MessageKind.Notification, 3, 0, 1)); return null; }
                catch (Exception e) { return e; }
            });
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            await Task.Run(() => session.CloseAsync());
            Assert.That(session.Completion.IsCompleted, Is.True);
        });

        [Test] public void AssemblyAndInputHaveNoNetworkEngineOrJsonDependencies()
        {
            foreach (var assembly in new[] { typeof(MessageSession).Assembly, typeof(RpcSession).Assembly })
                foreach (var dependency in assembly.GetReferencedAssemblies())
                    Assert.That(new[] { "PulletNet.ClientSDK", "PulletNet.NetCore", "PulletNet.Hosting", "UnityEngine", "Newtonsoft.Json" }
                        .Any(name => dependency.Name.StartsWith(name, StringComparison.Ordinal)), Is.False, dependency.Name);
            Assert.That(typeof(MessageSession).Assembly.GetReferencedAssemblies().Any(a => a.Name == "PulletNet.Messaging"), Is.True);
        }

        private static byte[] Frame(MessageKind kind, uint id, ulong rpc, int number)
        {
            var body = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(body, number);
            return MessageFrameCodec.Encode(Protocol, MessageEncoding.Protobuf, kind, id, rpc, body);
        }
        // Opaque 4-byte fixture codec; not a Protobuf implementation or platform codec acceptance.
        private sealed class ScalarCodec : IMessageCodec
        {
            internal int Encodes, Decodes;
            internal Action BeforeSerialize;
            internal byte[] ExpectedArray;
            internal bool BorrowedOriginalArray;
            public MessageEncoding Encoding => MessageEncoding.Protobuf;
            public void Serialize<T>(T value, IBufferWriter<byte> output)
            { Encodes++; BeforeSerialize?.Invoke(); BinaryPrimitives.WriteInt32LittleEndian(output.GetSpan(4), (int)(object)value); output.Advance(4); }
            public T Deserialize<T>(ReadOnlyMemory<byte> input)
            {
                Decodes++;
                if (ExpectedArray != null && MemoryMarshal.TryGetArray(input, out var segment))
                    BorrowedOriginalArray = ReferenceEquals(segment.Array, ExpectedArray);
                return (T)(object)BinaryPrimitives.ReadInt32LittleEndian(input.Span);
            }
        }
        private static async Task<Exception> Failure(Task task)
        {
            try { await task; } catch(Exception error) { return error; }
            Assert.Fail("Expected failure was not observed."); return null;
        }
        private static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var clock = Stopwatch.StartNew();
            while (!task.IsCompleted) { Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(10)); yield return null; }
            task.GetAwaiter().GetResult();
        }
    }
}
