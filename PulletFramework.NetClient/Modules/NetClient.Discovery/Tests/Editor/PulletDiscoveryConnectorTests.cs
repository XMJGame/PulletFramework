using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletFramework.Discovery;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Discovery.Tests
{
    public sealed class PulletDiscoveryConnectorTests
    {
        private GameObject _object;
        private PulletNetworkManager _manager;
        private PulletDiscoveryConnector _discovery;
        private Exception _expectedCloseFailure;
        [SetUp] public void Setup()
        {
            _expectedCloseFailure = null;
            _object = new GameObject("Optional discovery owner");
            _manager = _object.AddComponent<PulletNetworkManager>();
            _manager.connectOnStart = false; _manager.dontDestroyOnLoad = false;
            _manager.enableConnectionLogs = false; _manager.enableKeepAlive = false; _manager.enableReconnect = false;
            Call(_manager, "Awake");
            _discovery = _object.AddComponent<PulletDiscoveryConnector>();
            _discovery.connectOnStart = false; _discovery.enableLogs = false;
            Call(_discovery, "Awake");
        }
        [UnityTearDown] public IEnumerator Cleanup() => UnityTaskTest.Run(async () =>
        {
            try
            {
                try { await _discovery.CloseAsync(); }
                catch (AggregateException error) when (_expectedCloseFailure != null)
                { Assert.That(error.Flatten().InnerExceptions, Does.Contain(_expectedCloseFailure)); }
            }
            finally { try { await _manager.ShutdownAsync(); } finally { Object.DestroyImmediate(_object); } }
        });
        [Test] public void DefaultsEnumsAndOptionalRelayAreExplicit()
        {
            Assert.That((byte)AutoConnectMode.Direct, Is.EqualTo(0));
            Assert.That((byte)AutoConnectMode.DiscoverFirst, Is.EqualTo(1));
            Assert.That((byte)AutoConnectMode.DirectThenDiscover, Is.EqualTo(2));
            Assert.That(_discovery.ValidateConfiguration(out var error), Is.True, error);
            var relay = _object.AddComponent<PulletDiscoveryEventRelay>();
            Assert.That(relay.onDiscoveryStarted, Is.Not.Null);
            Assert.That(relay.onDiscoveryAttempt, Is.Not.Null);
            Assert.That(relay.onDiscoverySucceeded, Is.Not.Null);
            Assert.That(relay.onDiscoveryFailed, Is.Not.Null);
            Assert.That(relay.onDiscoveryStopped, Is.Not.Null);
        }
        [Test] public void InvalidDiscoveryAttemptsAreRejectedByOptionalOwner()
        {
            _discovery.discoveryMaxAttempts = 0;
            Assert.That(_discovery.ValidateConfiguration(out var error), Is.False);
            StringAssert.Contains("Discovery", error);
            _discovery.discoveryMaxAttempts = 33;
            Assert.That(_discovery.ValidateConfiguration(out error), Is.False);
        }
        [Test] public void TwoStartupOwnersAreRejectedRatherThanDoubleRun()
        {
            _manager.connectOnStart = true;
            Assert.That(_discovery.ValidateConfiguration(out var error), Is.False);
            StringAssert.Contains("one startup", error);
        }
        [UnityTest] public IEnumerator CloseJoinsActualDiscoveryWithoutAnotherFrame() => UnityTaskTest.Run(async () =>
        {
            using var server = Passive(); Configure(server, 30);
            var sent = server.ReceiveAsync(); var discovery = _discovery.DiscoverAsync(); await sent;
            var close = _discovery.CloseAsync();
            Assert.That(_discovery.CloseAsync(), Is.SameAs(close));
            await close;
            await Canceled(discovery);
            Assert.Throws<ObjectDisposedException>(() => _discovery.DiscoverAsync());
            Assert.That(_manager.IsShuttingDown, Is.False, "Discovery borrows the Manager; it must not close it.");
        });
        [UnityTest] public IEnumerator CloseInsideUiCallbackJoinsEnteredCallbackAndSkipsLaterListeners() => UnityTaskTest.Run(async () =>
        {
            using var server = Passive(); Configure(server, 30);
            var sent = server.ReceiveAsync(); var discovery = _discovery.DiscoverAsync(); await sent;
            Task close = null; bool completedInside = true; int later = 0;
            _discovery.DiscoveryStarted += () => { close = _discovery.CloseAsync(); completedInside = close.IsCompleted; };
            _discovery.DiscoveryStarted += () => later++;
            Call(_discovery, "Update");
            Assert.That(completedInside, Is.False); Assert.That(later, Is.Zero);
            await close; await Canceled(discovery);
        });
        [UnityTest] public IEnumerator CloseJoinsAcceptedManagerConnectWithoutStealingManagerOwnership() => UnityTaskTest.Run(async () =>
        {
            var coordinator = typeof(PulletNetworkManager).GetField("_connectionCoordinator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager);
            var gate = (SemaphoreSlim)coordinator.GetType().GetProperty("OperationGate").GetValue(coordinator);
            await gate.WaitAsync(); _discovery.enableDiscovery = false;
            try
            {
                var workflow = _discovery.AutoConnectAsync();
                Assert.That(workflow.IsCompleted, Is.False);
                var close = _discovery.CloseAsync(); await close;
                Assert.That((await workflow).ErrorCode, Is.EqualTo(ConnectionErrorCode.Canceled));
                Assert.That(_manager.IsShuttingDown, Is.False);
            }
            finally { gate.Release(); }
        });
        [UnityTest] public IEnumerator NewManagerCommandCancelsOldSelectionWithoutAnAutomaticSwitch() => UnityTaskTest.Run(async () =>
        {
            using var server = Passive(); Configure(server, 30);
            _discovery.autoConnectMode = AutoConnectMode.DiscoverFirst;
            var sent = server.ReceiveAsync(); var workflow = _discovery.AutoConnectAsync(); await sent;
            long before = _manager.ConnectionRevision;
            await _manager.DisconnectAsync();
            Assert.That(_manager.ConnectionRevision, Is.GreaterThan(before));
            Call(_discovery, "Update");
            Assert.That((await workflow).ErrorCode, Is.EqualTo(ConnectionErrorCode.Canceled));
            Assert.That(_manager.IsConnected, Is.False);
        });
        [UnityTest] public IEnumerator DiscoveryFactsAreOrderedOnUnityThreadAndCallbackFailuresAreIsolated() => UnityTaskTest.Run(async () =>
        {
            using var server = Passive(); Configure(server, .1f);
            var thread = Thread.CurrentThread.ManagedThreadId; var facts = new List<string>();
            _discovery.DiscoveryStarted += () => throw new InvalidOperationException("controlled UI");
            _discovery.CallbackError += _ => throw new InvalidOperationException("controlled diagnostic");
            _discovery.DiscoveryStarted += () => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(thread)); facts.Add("start"); };
            _discovery.DiscoveryAttempted += (_, __) => facts.Add("attempt");
            _discovery.DiscoveryFailed += _ => facts.Add("empty");
            _discovery.DiscoveryStopped += () => facts.Add("stop");
            var result = await _discovery.DiscoverAsync();
            Assert.That(result.Servers, Is.Empty); Assert.That(facts, Is.Empty);
            Call(_discovery, "Update");
            Assert.That(facts, Is.EqualTo(new[] { "start", "attempt", "empty", "stop" }));
            Assert.That(_discovery.CallbackFailures, Is.EqualTo(2));
        });
        [UnityTest] public IEnumerator DisableStopsCurrentIoButRelayDisableDoesNotOwnIt() => UnityTaskTest.Run(async () =>
        {
            using var server = Passive(); Configure(server, 30);
            var relay = _object.AddComponent<PulletDiscoveryEventRelay>(); Call(relay, "OnEnable");
            var sent = server.ReceiveAsync(); var discovery = _discovery.DiscoverAsync(); await sent;
            Call(relay, "OnDisable"); Assert.That(discovery.IsCompleted, Is.False);
            Call(_discovery, "OnDisable"); await _discovery.StopAsync(); await Canceled(discovery);
            Call(_discovery, "Update");
            Configure(server, .1f); Assert.That((await _discovery.DiscoverAsync()).Servers, Is.Empty);
        });
        [UnityTest] public IEnumerator DestroyRequestsPermanentCloseAndRealExit() => UnityTaskTest.Run(async () =>
        {
            using var server = Passive(); Configure(server, 30);
            var sent = server.ReceiveAsync(); var discovery = _discovery.DiscoverAsync(); await sent;
            Call(_discovery, "OnDestroy"); await _discovery.CloseAsync(); await Canceled(discovery);
        });
        [Test] public void CorePublicSurfaceDoesNotExposeDiscoveryTypes()
        {
            foreach(var reference in typeof(PulletNetworkManager).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Does.Not.Contain("Discovery"));
            Assert.That(typeof(PulletNetworkManager).GetMethod("DiscoverAsync"), Is.Null);
            Assert.That(typeof(PulletNetworkEventRelay).GetField("onDiscoveryStarted"), Is.Null);
        }
        [UnityTest] public IEnumerator ForeignCancellationIsAFaultAndCannotBeHiddenByClose() => UnityTaskTest.Run(async () =>
        {
            var field = typeof(PulletDiscoveryConnector).GetField("_discovery", BindingFlags.Instance | BindingFlags.NonPublic);
            await ((DiscoveryRuntime)field.GetValue(_discovery)).CloseAsync();
            using var foreign = new CancellationTokenSource(); foreign.Cancel();
            var cause = new OperationCanceledException(foreign.Token);
            _expectedCloseFailure = cause;
            Func<PulletNet.Discovery.DiscoveryOptions, Action<int, int>, CancellationToken, Task<PulletNet.Discovery.DiscoveryResult>> native =
                (_, __, ___) => Task.FromException<PulletNet.Discovery.DiscoveryResult>(cause);
            var constructor = typeof(DiscoveryRuntime).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { native.GetType(), typeof(int) }, null);
            var controlled = (DiscoveryRuntime)constructor.Invoke(new object[] { native, 16 });
            field.SetValue(_discovery, controlled);
            _discovery.autoConnectMode = AutoConnectMode.DiscoverFirst;
            var work = _discovery.AutoConnectAsync();
            try { await work; Assert.Fail("Foreign cancellation was treated as a normal result."); }
            catch (OperationCanceledException error) { Assert.That(error, Is.SameAs(cause)); }
            Assert.That(work.IsFaulted, Is.True, "Only the actual owner cancellation may become a canceled policy result.");
            try { await _discovery.CloseAsync(); Assert.Fail("Close masked the native fault."); }
            catch (AggregateException error) { Assert.That(error.Flatten().InnerExceptions, Does.Contain(cause)); }
        });
        private void Configure(UdpClient server, float timeout)
        {
            _discovery.discoveryAddress = "127.0.0.1";
            _discovery.discoveryPort = ((IPEndPoint)server.Client.LocalEndPoint).Port;
            _discovery.discoveryTimeoutSeconds = timeout;
            _discovery.discoveryMaxAttempts = 1; _discovery.discoverySendIntervalSeconds = .05f;
            _discovery.retryDiscoveryUntilConnected = false;
        }
        private static UdpClient Passive() => new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        private static async Task Canceled(Task task) { try { await task; Assert.Fail("Cancellation was hidden."); } catch(OperationCanceledException) { } }
        private static void Call(object owner, string method) => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
    }
}
