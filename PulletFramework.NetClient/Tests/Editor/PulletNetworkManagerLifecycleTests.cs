using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletNetworkManagerLifecycleTests
    {
        private GameObject _object;
        private PulletNetworkManager _manager;
        [SetUp] public void Setup()
        {
            _object = new GameObject("Manager owned lifecycle");
            _manager = _object.AddComponent<PulletNetworkManager>();
            _manager.connectOnStart = false; _manager.dontDestroyOnLoad = false;
            _manager.enableConnectionLogs = false; _manager.enableKeepAlive = false; _manager.enableReconnect = false;
            Invoke("Awake");
        }
        [UnityTearDown] public IEnumerator Cleanup() => UnityTaskTest.Run(async () =>
        {
            try { await _manager.ShutdownAsync(); } catch { /* individual tests assert intentional cleanup failures */ }
            Object.DestroyImmediate(_object);
        });

        [UnityTest] public IEnumerator ShutdownJoinsRecoveryAndCancellationAbandonsOnlyOneWaiter() => UnityTaskTest.Run(async () =>
        {
            var io = Signal(); var entered = Signal();
            await ReplaceRecovery(async (target, token) => { entered.TrySetResult(true); await io.Task; return ConnectionResult.Ok(); });
            var recovery = Get<PulletActiveServerRecovery>("_activeServerRecovery");
            Assert.That(recovery.Start(new ConnectionEndpoint("held", 0, 0, 0), .5f, "test", default), Is.True);
            await entered.Task;
            using var cancellation = new CancellationTokenSource();
            var waiter = _manager.ShutdownAsync(cancellation.Token); var shared = _manager.ShutdownAsync();
            try
            {
                cancellation.Cancel(); Assert.That(await Failure(waiter), Is.InstanceOf<OperationCanceledException>());
                Assert.That(shared.IsCompleted, Is.False); Assert.That(recovery.IsRunning, Is.True);
                Assert.That(_manager.ShutdownAsync(), Is.SameAs(shared));
                Assert.That((await _manager.ConnectAsync()).ErrorCode, Is.EqualTo(ConnectionErrorCode.Disposed));
                Assert.That(_manager.IsShuttingDown, Is.True);
            }
            finally { io.TrySetResult(true); await shared; }
            Assert.That(recovery.IsRunning, Is.False);
        });

        [UnityTest] public IEnumerator ShutdownFromApplicationCallbackJoinsThatCallback() => UnityTaskTest.Run(async () =>
        {
            Task shutdown = null; bool completedInside = true; int later = 0;
            Invoke("Enqueue", (Action)(() => { shutdown = _manager.ShutdownAsync(); completedInside = shutdown.IsCompleted; }));
            Invoke("Enqueue", (Action)(() => later++));
            Invoke("Update");
            Assert.That(completedInside, Is.False); Assert.That(later, Is.Zero);
            await shutdown; Assert.That(_manager.ShutdownAsync(), Is.SameAs(shutdown));
        });

        [UnityTest] public IEnumerator ShutdownFromPayloadCallbackJoinsOwnerAndStopsLaterSubscribers() => UnityTaskTest.Run(async () =>
        {
            var source = CreateSource(); var runtime = Get<PulletClientRuntime>("_runtime"); Open(runtime, source);
            Task shutdown = null; bool completedInside = true; int later = 0;
            _manager.PayloadReceived += _ => { shutdown = _manager.ShutdownAsync(); completedInside = shutdown.IsCompleted; };
            _manager.PayloadReceived += _ => later++;
            runtime.Receive(source, Payload()); Invoke("Update");
            Assert.That(completedInside, Is.False); Assert.That(later, Is.Zero); await shutdown;
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
            Assert.That(runtime.GetSnapshot().Payloads, Is.Zero);
        });

        [UnityTest] public IEnumerator SessionBoundaryInsidePayloadPreventsStaleSecondSubscriber() => UnityTaskTest.Run(async () =>
        {
            var source = CreateSource(); var runtime = Get<PulletClientRuntime>("_runtime"); Open(runtime, source);
            int first = 0, second = 0;
            _manager.PayloadReceived += _ => { first++; runtime.OnState(source, new ConnectionStateChangedEvent(ConnectionState.Connected, ConnectionState.Disconnected)); };
            _manager.PayloadReceived += _ => second++;
            runtime.Receive(source, Payload()); Invoke("Update");
            Assert.That(first, Is.EqualTo(1)); Assert.That(second, Is.Zero);
            Assert.That(runtime.GetSnapshot().Payloads, Is.Zero); await _manager.ShutdownAsync();
        });

        [UnityTest] public IEnumerator ReliableOverflowRemainsFailedOnShutdownWithoutAnotherFrame() => UnityTaskTest.Run(async () =>
        {
            _manager.payloadQueueCapacity = 1;
            var source = CreateSource(); var runtime = Get<PulletClientRuntime>("_runtime"); Open(runtime, source);
            runtime.Receive(source, Payload()); runtime.Receive(source, Payload());
            var error = await Failure(_manager.ShutdownAsync());
            StringAssert.Contains("ReliableOverflow", error.ToString());
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
            Assert.That(runtime.GetSnapshot().Payloads, Is.Zero);
        });

        [UnityTest] public IEnumerator CancellationCallbackFailureStillJoinsOperationsAndRuntime() => UnityTaskTest.Run(async () =>
        {
            var source = CreateSource(); var held = (IDisposable)Invoke("EnterOperation", default(CancellationToken));
            var cause = new InvalidOperationException("controlled manager cancellation callback");
            using var registration = Get<CancellationTokenSource>("_lifetimeCts").Token.Register(() => throw cause);
            var shutdown = _manager.ShutdownAsync();
            try { Assert.That(shutdown.IsCompleted, Is.False, "Accepted application operation still owns its exit."); }
            finally { held.Dispose(); }
            var error = await Failure(shutdown);
            Assert.That(((AggregateException)error).Flatten().InnerExceptions, Does.Contain(cause));
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
        });

        [UnityTest] public IEnumerator PreCanceledShutdownDoesNotStartAndClosedFacadeRejectsNewWork() => UnityTaskTest.Run(async () =>
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.That(await Failure(_manager.ShutdownAsync(canceled.Token)), Is.InstanceOf<OperationCanceledException>());
            var source = CreateSource(); Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disconnected));
            await _manager.ShutdownAsync();
            Assert.That((await _manager.ConnectAsync()).ErrorCode, Is.EqualTo(ConnectionErrorCode.Disposed));
            Assert.That((await _manager.ConnectAsync(new ConnectionEndpoint("127.0.0.1", 1, 2, 3))).ErrorCode, Is.EqualTo(ConnectionErrorCode.Disposed));
            Assert.That((await _manager.DisconnectAsync()).ErrorCode, Is.EqualTo(ConnectionErrorCode.Disposed));
            Assert.That((await _manager.SuspendAsync()).ErrorCode, Is.EqualTo(ConnectionErrorCode.Disposed));
            Assert.That((await _manager.ResumeAsync()).ErrorCode, Is.EqualTo(ConnectionErrorCode.Disposed));
            Assert.That((await _manager.SendAsync(new byte[] { 1 })).IsSuccess, Is.False);
            Assert.That(_manager.IsShuttingDown, Is.True);
        });

        [UnityTest] public IEnumerator DestroyRequestsShutdownButDoesNotClaimRecoveryExited() => UnityTaskTest.Run(async () =>
        {
            var io = Signal();
            await ReplaceRecovery(async (target, token) => { await io.Task; return ConnectionResult.Ok(); });
            var recovery = Get<PulletActiveServerRecovery>("_activeServerRecovery");
            recovery.Start(new ConnectionEndpoint("held", 0, 0, 0), .5f, "test", default);
            Invoke("OnDestroy"); var shutdown = _manager.ShutdownAsync();
            try { Assert.That(shutdown.IsCompleted, Is.False); Assert.That(PulletNetworkManager.Instance, Is.Null); }
            finally { io.TrySetResult(true); await shutdown; }
            Assert.That(recovery.IsRunning, Is.False);
        });

        [UnityTest] public IEnumerator ConcurrentShutdownRequestsPublishOnlyOneTask() => UnityTaskTest.Run(async () =>
        {
            var io = Signal(); await ReplaceRecovery(async (target, token) => { await io.Task; return ConnectionResult.Ok(); });
            Get<PulletActiveServerRecovery>("_activeServerRecovery").Start(new ConnectionEndpoint("held", 0, 0, 0), .5f, "test", default);
            var tasks = new Task[12]; var dispatch = new Task[12];
            for (int i = 0; i < tasks.Length; i++) { int index = i; dispatch[i] = Task.Run(() => { tasks[index] = _manager.ShutdownAsync(); }); }
            try
            {
                await Task.WhenAll(dispatch);
                foreach (var task in tasks) { Assert.That(task, Is.SameAs(tasks[0])); Assert.That(task.IsCompleted, Is.False); }
            }
            finally { io.TrySetResult(true); await _manager.ShutdownAsync(); }
        });

        [UnityTest] public IEnumerator CombinedPauseFocusUsesLatestIntentAndShutdownJoinsQueuedPolicy() => UnityTaskTest.Run(async () =>
        {
            CreateSource(); var coordinator = Get<PulletConnectionCoordinator>("_connectionCoordinator");
            await coordinator.OperationGate.WaitAsync();
            try
            {
                Invoke("OnApplicationPause", true); Invoke("OnApplicationFocus", false); Invoke("OnApplicationPause", false);
                Assert.That(Get<int>("_applicationPauseReasons"), Is.EqualTo(2), "Focus loss still keeps application backgrounded.");
                Invoke("OnApplicationFocus", true); Assert.That(Get<int>("_applicationPauseReasons"), Is.Zero);
                var shutdown = _manager.ShutdownAsync(); await shutdown;
                Assert.That(Get<int>("_operations"), Is.Zero);
            }
            finally { coordinator.OperationGate.Release(); }
        });

        [Test] public void ApplicationActionsAreOrderedAndCallbackFailureDoesNotSkipTheNextAction()
        {
            var delivered = new List<int>();
            Invoke("Enqueue", (Action)(() => delivered.Add(1)));
            Invoke("Enqueue", (Action)(() => throw new InvalidOperationException("controlled application callback")));
            Invoke("Enqueue", (Action)(() => delivered.Add(2)));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: controlled application callback"));
            Invoke("Update"); Assert.That(delivered, Is.EqualTo(new[] { 1, 2 }));
        }


        [UnityTest] public IEnumerator ApplicationEventOverflowHasAnObservableTerminalFailureWithoutUpdate() => UnityTaskTest.Run(async () =>
        {
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Manager application event capacity reached"));
            for (int i = 0; i < 129; i++) Invoke("Enqueue", (Action)(() => { }));
            var shutdown = _manager.ShutdownAsync(); var error = await Failure(shutdown);
            StringAssert.Contains("application event capacity", error.ToString());
            Assert.That(Get<Queue<Action>>("_applicationEvents").Count, Is.Zero);
            Assert.That(_manager.ShutdownAsync(), Is.SameAs(shutdown));
        });

        private async Task ReplaceRecovery(Func<ConnectionEndpoint, CancellationToken, Task<ConnectionResult>> connect)
        {
            await Get<PulletActiveServerRecovery>("_activeServerRecovery").StopAsync();
            Set("_activeServerRecovery", new PulletActiveServerRecovery(() => false, connect, () => Task.CompletedTask,
                () => true, _ => { }, _ => { }, _ => { }, (duration, token) => Task.CompletedTask));
        }
        private HostedClient CreateSource()
        { Invoke("GetOrCreateClient", "127.0.0.1", 7778, 7777, 7779, 0L); return Get<HostedClient>("_source"); }
        private static void Open(PulletClientRuntime runtime, HostedClient source)
            => runtime.OnState(source, new ConnectionStateChangedEvent(ConnectionState.Handshaking, ConnectionState.Connected));
        private static ReceivedPayload Payload() => new ReceivedPayload(new byte[] { 1 }, TransportKind.Udp, ChannelType.ReliableOrdered, 0);
        private T Get<T>(string field) => (T)typeof(PulletNetworkManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager);
        private void Set(string field, object value) => typeof(PulletNetworkManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_manager, value);
        private object Invoke(string method, params object[] args) => typeof(PulletNetworkManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_manager, args);
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static async Task<Exception> Failure(Task task)
        { try { await task; } catch (Exception error) { return error; } Assert.Fail("Expected failure was not observed."); return null; }
    }
}
