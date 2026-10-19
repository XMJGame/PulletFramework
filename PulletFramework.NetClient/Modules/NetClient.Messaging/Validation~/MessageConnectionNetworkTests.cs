using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using PulletNet.Messaging;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Messaging.Tests
{
    // Explicit fixture validation only: not imported into user projects by default.
    public sealed class MessageConnectionNetworkTests
    {
        [UnityTest] public IEnumerator UdpRpcAndNotificationUseTheExistingRuntimeQueue() => MessageConnectionTests.Run(() => Exchange(ConnectionMode.UdpOnly));
        [UnityTest] public IEnumerator TcpRpcAndNotificationUseTheExistingRuntimeQueue() => MessageConnectionTests.Run(() => Exchange(ConnectionMode.TcpOnly));
        [UnityTest] public IEnumerator WebSocketRpcAndNotificationUseTheExistingRuntimeQueue() => MessageConnectionTests.Run(() => Exchange(ConnectionMode.WebSocketOnly));
        private static async Task Exchange(ConnectionMode mode)
        {
            await using var runtime = new PulletClientRuntime();
            await using var connection = MessageConnectionTests.Create(runtime, options: Network(mode));
            int notifications = 0, wrongRouter = 0;
            connection.Session.Subscribe<int>(3, value => { Assert.That(value, Is.EqualTo(7)); notifications++; });
            connection.Session.Subscribe<int>(2, _ => wrongRouter++);
            var connect = await connection.ConnectAsync();
            Assert.That(connect.IsSuccess, Is.True, connect.Message);
            Assert.That(connect.SessionContinuity, Is.EqualTo(SessionContinuity.NewSession));
            Assert.That(connection.IsReady, Is.True);
            Assert.That((await connection.ConnectAsync()).ErrorCode, Is.EqualTo(ConnectionErrorCode.InvalidState));
            var call = connection.Session.CallAsync<int, int>(1, 2, 41, MessageConnectionTests.Deadline);
            await PumpUntil(() => call.IsCompleted, runtime);
            Assert.That(await call, Is.EqualTo(42)); Assert.That(wrongRouter, Is.Zero);
            Assert.That(await connection.Session.SendNotificationAsync(3, 7, MessageConnectionTests.Deadline), Is.EqualTo(MessageSendResult.Accepted));
            await PumpUntil(() => notifications == 1, runtime);
            await connection.CloseAsync();
            Assert.That(connection.Session.RpcSnapshot.ActiveCalls, Is.Zero);
            Assert.That(connection.Session.RpcSnapshot.ReservedBytes, Is.Zero);
            Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero);
            Assert.That(runtime.GetSnapshot().RegisteredClients, Is.Zero);
        }

        [UnityTest] public IEnumerator CloseInsideNotificationJoinsRuntimeCallbackAndSkipsLaterHandlers() => MessageConnectionTests.Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            var connection = MessageConnectionTests.Create(runtime, options: Network(ConnectionMode.TcpOnly));
            Task close = null; int later = 0, raw = 0; bool completedInside = false;
            connection.Session.Subscribe<int>(3, _ =>
            {
                close = connection.CloseAsync();
                completedInside = close.IsCompleted;
            });
            connection.Session.Subscribe<int>(3, _ => later++);
            runtime.PayloadReceived += _ => raw++;
            Assert.That((await connection.ConnectAsync()).IsSuccess, Is.True);
            await connection.Session.SendNotificationAsync(3, 7, MessageConnectionTests.Deadline);
            await PumpUntil(() => close != null, runtime);
            await close; // no further Update needed
            Assert.That(completedInside, Is.False, "Cannot claim callback exit from inside itself.");
            Assert.That(later, Is.Zero); Assert.That(raw, Is.Zero, "Typed close must suppress later raw Runtime subscribers too.");
            Assert.That(connection.Session.NotificationSnapshot.OccupiedSubscriptions, Is.Zero);
            Assert.That(runtime.GetSnapshot().ConsumingPayloads, Is.Zero);
        });

        [UnityTest] public IEnumerator TwoBindingsHaveIndependentRpcOneAndClosingOneDoesNotStopTheOther() => MessageConnectionTests.Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            await using var a = MessageConnectionTests.Create(runtime, "a", Network(ConnectionMode.TcpOnly));
            await using var b = MessageConnectionTests.Create(runtime, "b", Network(ConnectionMode.UdpOnly));
            Assert.That((await a.ConnectAsync()).IsSuccess, Is.True); Assert.That((await b.ConnectAsync()).IsSuccess, Is.True);
            var first = a.Session.CallAsync<int, int>(1, 2, 10, MessageConnectionTests.Deadline);
            var second = b.Session.CallAsync<int, int>(1, 2, 20, MessageConnectionTests.Deadline);
            await PumpUntil(() => first.IsCompleted && second.IsCompleted, runtime);
            Assert.That(await first, Is.EqualTo(11)); Assert.That(await second, Is.EqualTo(21));
            await a.CloseAsync(); Assert.That(b.IsReady, Is.True);
            second = b.Session.CallAsync<int, int>(1, 2, 30, MessageConnectionTests.Deadline);
            await PumpUntil(() => second.IsCompleted, runtime); Assert.That(await second, Is.EqualTo(31));
        });

        [UnityTest] public IEnumerator RuntimeCloseCancelsPendingRpcWithoutAnotherFrame() => MessageConnectionTests.Run(async () =>
        {
            var runtime = new PulletClientRuntime(); var connection = MessageConnectionTests.Create(runtime, options: Network(ConnectionMode.TcpOnly));
            try
            {
                Assert.That((await connection.ConnectAsync()).IsSuccess, Is.True);
                var call = connection.Session.CallAsync<int, int>(9, 2, 5, TimeSpan.FromSeconds(30)); // fixture deliberately omits response 9
                Assert.That(connection.Session.RpcSnapshot.PendingCalls, Is.EqualTo(1));
                await runtime.CloseAsync(); await connection.Completion;
                try { await call; Assert.Fail("Pending RPC must retire"); } catch (ObjectDisposedException) { }
                Assert.That(connection.Session.RpcSnapshot.ActiveCalls, Is.Zero);
                Assert.That(connection.Session.RpcSnapshot.ReservedBytes, Is.Zero);
            }
            finally { await connection.CloseAsync(); await runtime.CloseAsync(); }
        });

        [UnityTest] public IEnumerator OldQueuedPayloadCannotEnterTheReplacementWithTheSameName() => MessageConnectionTests.Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            var old = MessageConnectionTests.Create(runtime, options: Network(ConnectionMode.TcpOnly));
            int oldCalls = 0, newCalls = 0;
            old.Session.Subscribe<int>(3, _ => oldCalls++);
            Assert.That((await old.ConnectAsync()).IsSuccess, Is.True);
            await old.Session.SendNotificationAsync(3, 1, MessageConnectionTests.Deadline);
            await PumpUntil(() => runtime.GetSnapshot().Payloads > 0, runtime, pump: false);
            await old.CloseAsync();
            await using var replacement = MessageConnectionTests.Create(runtime, options: Network(ConnectionMode.TcpOnly));
            replacement.Session.Subscribe<int>(3, value => { Assert.That(value, Is.EqualTo(2)); newCalls++; });
            Assert.That((await replacement.ConnectAsync()).IsSuccess, Is.True);
            await replacement.Session.SendNotificationAsync(3, 2, MessageConnectionTests.Deadline);
            await PumpUntil(() => newCalls == 1, runtime);
            Assert.That(oldCalls, Is.Zero); Assert.That(newCalls, Is.EqualTo(1));
            Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero);
        });

        [UnityTest] public IEnumerator BypassingTheBindingRetiresItInsteadOfResettingItsRegistry() => MessageConnectionTests.Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            var connection = MessageConnectionTests.Create(runtime, options: Network(ConnectionMode.UdpOnly));
            Assert.That((await connection.ConnectAsync()).IsSuccess, Is.True);
            var original = connection.Session.Source;
            Assert.That(runtime.TryGetClient(connection.Key, out var borrowed), Is.True);
            // Deliberate misuse: production callers must not drive this borrowed source.
            await borrowed.Client.DisconnectAsync();
            await connection.Completion;
            Assert.That(connection.IsReady, Is.False);
            Assert.That(connection.Session.Source, Is.SameAs(original));
            Assert.That(connection.Session.IsClosing, Is.True);
        });

        [UnityTest] public IEnumerator MalformedInputIsObservableAndDiagnosticFailureStaysIsolated() => MessageConnectionTests.Run(async () =>
        {
            await using var runtime = new PulletClientRuntime(); int diagnostics = 0, errors = 0, notices = 0;
            await using var connection = new PulletMessageConnection(runtime, "typed", Network(ConnectionMode.TcpOnly),
                MessageConnectionTests.Protocol, new MessageConnectionTests.ScalarCodec(), inputDiagnostic: result =>
                {
                    Assert.That(result.Kind, Is.EqualTo(PulletFramework.Messaging.MessageInputKind.InvalidFrame));
                    Assert.That(result.FrameError, Is.EqualTo(FrameError.Truncated));
                    diagnostics++; throw new InvalidOperationException("injected input diagnostic");
                });
            runtime.CallbackError += _ => errors++;
            connection.Session.Subscribe<int>(3, _ => notices++);
            Assert.That((await connection.ConnectAsync()).IsSuccess, Is.True);
            Assert.That(runtime.TryGetClient("typed", out var borrowed), Is.True);
            Assert.That((await borrowed.Client.SendAsync(new byte[] { 1, 2, 3 })).IsSuccess, Is.True);
            await PumpUntil(() => diagnostics == 1, runtime);
            Assert.That(errors, Is.EqualTo(1)); Assert.That(notices, Is.Zero);
            Assert.That(connection.IsReady, Is.True); Assert.That(connection.Session.IsClosing, Is.False);
            Assert.That(runtime.GetSnapshot().PayloadBytes, Is.Zero);
        });

        [UnityTest] public IEnumerator ClosingOnlyMessagesNeverClaimsItClosedTheBorrowedNetworkClient() => MessageConnectionTests.Run(async () =>
        {
            await using var runtime = new PulletClientRuntime();
            await using var connection = MessageConnectionTests.Create(runtime, options: Network(ConnectionMode.TcpOnly));
            Assert.That((await connection.ConnectAsync()).IsSuccess, Is.True);
            await connection.Session.CloseAsync();
            Assert.That(connection.IsReady, Is.False);
            Assert.That(connection.Completion.IsCompleted, Is.False);
            Assert.That(runtime.TryGetClient(connection.Key, out var source), Is.True);
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Connected));
            await connection.CloseAsync();
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
        });

        private static NetClientOptions Network(ConnectionMode mode)
        {
            var config = Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS");
            Assert.That(string.IsNullOrEmpty(config), Is.False, "Controlled fixture ports required.");
            var ports = JsonUtility.FromJson<Ports>(File.ReadAllText(config));
            var options = MessageConnectionTests.Options();
            options.Connection = new ConnectionOptions { Host = "127.0.0.1", ConnectionMode = mode, TcpPort = ports.tcp, UdpPort = ports.udp, WsPort = ports.ws };
            return options;
        }
        private static async Task PumpUntil(Func<bool> condition, PulletClientRuntime runtime, bool pump = true)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Expected boundary was not observed.");
                if (pump) runtime.Update();
                await Task.Yield();
            }
        }
        [Serializable] private sealed class Ports { public int tcp; public int udp; public int ws; }
    }
}
