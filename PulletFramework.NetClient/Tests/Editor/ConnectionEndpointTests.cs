using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Tests
{
    public sealed class ConnectionEndpointTests
    {
        private GameObject _object;
        private PulletNetworkManager _manager;

        [SetUp] public void Setup()
        {
            _object = new GameObject("Neutral endpoint test");
            _manager = _object.AddComponent<PulletNetworkManager>();
            _manager.connectOnStart = false;
            _manager.dontDestroyOnLoad = false;
            _manager.enableConnectionLogs = false;
            if (Get<CancellationTokenSource>("_lifetimeCts") == null)
                typeof(PulletNetworkManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_manager, null);
        }

        [UnityTearDown] public IEnumerator Cleanup() => UnityTaskTest.Run(async () =>
        {
            try { await _manager.ShutdownAsync(); }
            finally { Object.DestroyImmediate(_object); }
        });

        [Test] public void EndpointIsImmutableAndContainsOnlyConnectionAuthority()
        {
            var endpoint = new ConnectionEndpoint("host", 1, 2, 3);
            Assert.That(endpoint.Host, Is.EqualTo("host"));
            Assert.That(endpoint.TcpPort, Is.EqualTo(1));
            Assert.That(endpoint.UdpPort, Is.EqualTo(2));
            Assert.That(endpoint.WebSocketPort, Is.EqualTo(3));
            foreach (var property in typeof(ConnectionEndpoint).GetProperties())
                Assert.That(property.SetMethod, Is.Null, property.Name);
            Assert.That(typeof(ConnectionEndpoint).GetFields(BindingFlags.Public | BindingFlags.Instance), Is.Empty);
            Assert.That(typeof(ConnectionEndpoint).GetProperties().Length, Is.EqualTo(4));
        }

        [Test] public void NullEndpointIsRejectedExplicitly()
            => Assert.Throws<ArgumentNullException>(() => _manager.ConnectAsync((ConnectionEndpoint)null));

        [UnityTest] public IEnumerator EndpointDoesNotFallbackToInspectorPorts() => UnityTaskTest.Run(async () =>
        {
            foreach (var mode in new[] { ConnectionMode.UdpOnly, ConnectionMode.TcpOnly, ConnectionMode.WebSocketOnly })
            {
                _manager.connectionMode = mode;
                var result = await _manager.ConnectAsync(new ConnectionEndpoint("127.0.0.1", 0, 0, 0));
                Assert.That(result.ErrorCode, Is.EqualTo(ConnectionErrorCode.InvalidState), mode.ToString());
                Assert.That(Get<HostedClient>("_source"), Is.Null);
                Assert.That(_manager.ActiveEndpoint, Is.Null, "A rejected candidate is not a successful endpoint.");
            }
        });

        [UnityTest] public IEnumerator PreCanceledRequestDoesNotSupersedeExistingIntent() => UnityTaskTest.Run(async () =>
        {
            var coordinator = Get<PulletConnectionCoordinator>("_connectionCoordinator");
            var intent = coordinator.BeginConnection(CancellationToken.None);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                var result = await _manager.ConnectAsync(new ConnectionEndpoint("127.0.0.1", 1, 2, 3), canceled.Token);
                Assert.That(result.ErrorCode, Is.EqualTo(ConnectionErrorCode.Canceled));
                Assert.That(coordinator.Version, Is.EqualTo(intent.Version));
                Assert.That(intent.Token.IsCancellationRequested, Is.False);
                Assert.That(Get<int>("_operations"), Is.Zero);
            }
            finally { coordinator.Complete(intent); }
        });

        [UnityTest] public IEnumerator CancellationWhileWaitingForGateSettlesAcceptedOperation() => UnityTaskTest.Run(async () =>
        {
            var coordinator = Get<PulletConnectionCoordinator>("_connectionCoordinator");
            await coordinator.OperationGate.WaitAsync();
            using var cancel = new CancellationTokenSource();
            try
            {
                var request = _manager.ConnectAsync(new ConnectionEndpoint("127.0.0.1", 1, 2, 3), cancel.Token);
                Assert.That(request.IsCompleted, Is.False, "The controlled gate, not a real socket deadline, holds the operation.");
                Assert.That(Get<int>("_operations"), Is.EqualTo(1));
                cancel.Cancel();
                var result = await request;
                Assert.That(result.ErrorCode, Is.EqualTo(ConnectionErrorCode.Canceled));
                Assert.That(Get<int>("_operations"), Is.Zero);
                Assert.That(Get<HostedClient>("_source"), Is.Null);
            }
            finally { coordinator.OperationGate.Release(); }
        });

        [UnityTest] public IEnumerator ShutdownJoinsEndpointRequestWithoutWaitingForExternalGateOwner() => UnityTaskTest.Run(async () =>
        {
            var coordinator = Get<PulletConnectionCoordinator>("_connectionCoordinator");
            await coordinator.OperationGate.WaitAsync();
            try
            {
                var request = _manager.ConnectAsync(new ConnectionEndpoint("127.0.0.1", 1, 2, 3));
                Assert.That(request.IsCompleted, Is.False);
                var shutdown = _manager.ShutdownAsync();
                await shutdown;
                Assert.That(request.IsCompleted, Is.True);
                Assert.That((await request).ErrorCode, Is.EqualTo(ConnectionErrorCode.Canceled));
                Assert.That(Get<int>("_operations"), Is.Zero);
                Assert.That(_manager.ShutdownAsync(), Is.SameAs(shutdown));
                Assert.That(Get<HostedClient>("_source"), Is.Null);
            }
            finally { coordinator.OperationGate.Release(); }
        });

        [UnityTest] public IEnumerator RetiringDisconnectedSourceJoinsSdkDisposalWithoutAConnectionCommand() => UnityTaskTest.Run(async () =>
        {
            typeof(PulletNetworkManager).GetMethod("GetOrCreateClient", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_manager, new object[] { "127.0.0.1", 1, 2, 3, 0L });
            var source = Get<HostedClient>("_source");
            long revision = _manager.ConnectionRevision;
            await _manager.RetireDisconnectedClientAsync();
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed));
            Assert.That(Get<HostedClient>("_source"), Is.Null);
            Assert.That(_manager.ConnectionRevision, Is.EqualTo(revision));
        });

        [UnityTest] public IEnumerator ANewCommandSupersedesQueuedRetirement() => UnityTaskTest.Run(async () =>
        {
            var coordinator = Get<PulletConnectionCoordinator>("_connectionCoordinator");
            await coordinator.OperationGate.WaitAsync();
            var retire = _manager.RetireDisconnectedClientAsync();
            var disconnect = _manager.DisconnectAsync();
            coordinator.OperationGate.Release();
            try { await retire; Assert.Fail("Stale retirement was accepted."); }
            catch (OperationCanceledException) { }
            Assert.That((await disconnect).IsSuccess, Is.True);
            Assert.That(Get<int>("_operations"), Is.Zero);
        });

        private T Get<T>(string field)
            => (T)typeof(PulletNetworkManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager);
    }
}
