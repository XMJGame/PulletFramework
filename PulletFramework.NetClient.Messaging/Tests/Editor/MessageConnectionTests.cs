using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using PulletNet.Messaging;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Messaging.Tests
{
    public sealed class MessageConnectionTests
    {
        internal static readonly MessageProtocolOptions Protocol = new MessageProtocolOptions(0x1234, 1, 4);
        internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
        internal static NetClientOptions Options() => new NetClientOptions { Reconnect = new ReconnectOptions { EnableReconnect = false }, KeepAlive = new KeepAliveOptions { EnableKeepAlive = false } };
        internal static PulletMessageConnection Create(PulletClientRuntime runtime, string key = "typed", NetClientOptions options = null)
            => new PulletMessageConnection(runtime, key, options ?? Options(), Protocol, new ScalarCodec());

        [UnityTest] public IEnumerator CreationDoesNotConnectAndClosePreservesOtherSources() => Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            var other = runtime.Create("raw", Options());
            var binding = Create(runtime);
            Assert.That(runtime.TryGetClient(binding.Key, out var source), Is.True);
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(binding.IsReady, Is.False);
            await binding.CloseAsync();
            Assert.That(binding.Completion.IsCompleted, Is.True);
            Assert.That(binding.Session.Completion.IsCompleted, Is.True);
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
            Assert.That(other.Client.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(runtime.TryGetClient("raw", out _), Is.True);
        });
        [UnityTest] public IEnumerator RuntimeCloseOwnsNetworkAndBindingStillJoinsItsMessages() => Run(async () =>
        {
            var runtime = new PulletClientRuntime(); var binding = Create(runtime);
            await runtime.CloseAsync(); await binding.Completion;
            Assert.That(binding.IsClosing, Is.True);
            Assert.That(binding.Session.IsClosing, Is.True);
            Assert.That(binding.Session.RpcSnapshot.ActiveCalls, Is.Zero);
            Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
        });
        [UnityTest] public IEnumerator ReconnectAndLaneValidationNeverMutateOptionsOrCreateAClient() => Run(async () =>
        {
            await using var runtime = new PulletClientRuntime(); var options = new NetClientOptions();
            Assert.Throws<ArgumentException>(() => Create(runtime, options: options));
            Assert.That(options.Reconnect.EnableReconnect, Is.True);
            options.Reconnect.EnableReconnect = false;
            Assert.Throws<ArgumentException>(() => new PulletMessageConnection(runtime, "bad", options, Protocol, new ScalarCodec(), channelId: 1));
            Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
        });
        [UnityTest] public IEnumerator PreCanceledConnectAndCloseLeaveAnUnstartedBindingUsable() => Run(async () =>
        {
            await using var runtime = new PulletClientRuntime(); var binding = Create(runtime);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.That((await binding.ConnectAsync(canceled.Token)).ErrorCode, Is.EqualTo(ConnectionErrorCode.Canceled));
            try { await binding.CloseAsync(canceled.Token); Assert.Fail("Expected cancellation"); } catch (OperationCanceledException) { }
            Assert.That(binding.IsClosing, Is.False); Assert.That(binding.Completion.IsCompleted, Is.False);
            await binding.CloseAsync();
        });
        [UnityTest] public IEnumerator ClosingTheOldBindingCannotRemoveAReusedName() => Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            var old = Create(runtime); var oldSource = old.Session.Source;
            await old.CloseAsync();
            var replacement = Create(runtime);
            Assert.That(replacement.Session.Source, Is.Not.SameAs(oldSource));
            Assert.That(old.CloseAsync(), Is.SameAs(old.Completion));
            await old.CloseAsync();
            Assert.That(runtime.TryGetClient("typed", out var current), Is.True);
            Assert.That(current.Client.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(replacement.IsClosing, Is.False);
            await replacement.CloseAsync();
        });
        [UnityTest] public IEnumerator CloseFromCodecJoinsPreparationAndACanceledWaiterCannotCancelIt() => Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            PulletMessageConnection binding = null; Task waiter = null; bool completedInside = false;
            using var canceled = new CancellationTokenSource();
            var codec = new ScalarCodec { BeforeSerialize = () =>
            {
                waiter = binding.CloseAsync(canceled.Token); canceled.Cancel();
                completedInside = binding.Completion.IsCompleted;
            }};
            binding = new PulletMessageConnection(runtime, "typed", Options(), Protocol, codec);
            try { await binding.Session.CallAsync<int, int>(1, 2, 5, Deadline); Assert.Fail("Expected retirement"); }
            catch (ObjectDisposedException) { }
            try { await waiter; Assert.Fail("Expected waiter cancellation"); } catch (OperationCanceledException) { }
            await binding.Completion;
            Assert.That(completedInside, Is.False);
            Assert.That(binding.Session.RpcSnapshot.ActiveCalls, Is.Zero);
            Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
        });
        [UnityTest] public IEnumerator SendingBeforeConnectIsRejectedAndItsBudgetSettles() => Run(async () =>
        {
            await using var runtime = new PulletClientRuntime(); await using var binding = Create(runtime);
            Assert.That(await binding.Session.SendNotificationAsync(3, 5, Deadline), Is.EqualTo(MessageSendResult.Rejected));
            Assert.That(binding.Session.RpcSnapshot.ActiveCalls, Is.Zero);
        });
        [Test] public void CompositionAssemblyHasNoJsonOrSecondNativeClientHost()
        {
            var assembly = typeof(PulletMessageConnection).Assembly;
            foreach (var reference in assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Does.Not.Contain("Newtonsoft").And.Not.Contain("NetCore").And.Not.Contain("UnityEngine"));
            foreach (var field in typeof(PulletMessageConnection).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(NetClientHost)).And.Not.EqualTo(typeof(PulletNet.ClientSDK.NetClient)));
        }

        internal sealed class ScalarCodec : IMessageCodec
        {
            internal Action BeforeSerialize;
            public MessageEncoding Encoding => MessageEncoding.Protobuf; // fixture discriminator only, NOT a protobuf implementation
            public void Serialize<T>(T value, IBufferWriter<byte> destination)
            { BeforeSerialize?.Invoke(); BinaryPrimitives.WriteInt32LittleEndian(destination.GetSpan(4), (int)(object)value); destination.Advance(4); }
            public T Deserialize<T>(ReadOnlyMemory<byte> bytes) => (T)(object)BinaryPrimitives.ReadInt32LittleEndian(bytes.Span);
        }
        internal static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var watch = Stopwatch.StartNew();
            while (!task.IsCompleted) { Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Owner did not exit."); yield return null; }
            task.GetAwaiter().GetResult();
        }
    }
}
