using System.Reflection;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletNetworkManagerTests
    {
        private GameObject _gameObject;
        private PulletNetworkManager _manager;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("PulletNetworkManager Test");
            _manager = _gameObject.AddComponent<PulletNetworkManager>();
            _manager.connectOnStart = false;
            _manager.dontDestroyOnLoad = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Defaults_AreValid()
        {
            Assert.That(_manager.ValidateConfiguration(out string error), Is.True, error);
            Assert.That(_manager.connectionMode, Is.EqualTo(ConnectionMode.UdpWithTcpControl));
            Assert.That(_manager.autoConnectMode, Is.EqualTo(AutoConnectMode.DirectThenDiscover));
            Assert.That(_manager.discoveryMaxAttempts, Is.GreaterThan(0));
            Assert.That(_manager.discoverySendIntervalSeconds, Is.GreaterThan(0f));
            Assert.That(_manager.enableReconnect, Is.True);
        }

        [Test]
        public void CodeEvents_AreExposedByManagerAndInspectorEventsLiveInOptionalRelay()
        {
            PulletNetworkEventRelay relay = _gameObject.AddComponent<PulletNetworkEventRelay>();
            Assert.That(relay.onConnected, Is.Not.Null);
            Assert.That(relay.onConnectFailed, Is.Not.Null);
            Assert.That(relay.onDisconnected, Is.Not.Null);
            Assert.That(relay.onDiscoveryStarted, Is.Not.Null);
            Assert.That(relay.onDiscoverySucceeded, Is.Not.Null);
            Assert.That(relay.onDiscoveryFailed, Is.Not.Null);
            Assert.That(relay.onDiscoveryStopped, Is.Not.Null);
        }

        [Test]
        public void PackagePrefab_UsesReusableDefaults()
        {
            const string prefabPath =
                "Packages/com.xmjgame.pullet-framework.netclient/Runtime/Prefabs/PulletNetworkManager.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            Assert.That(prefab, Is.Not.Null, $"Missing package prefab: {prefabPath}");
            PulletNetworkManager manager = prefab.GetComponent<PulletNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.tcpPort, Is.EqualTo(7778));
            Assert.That(manager.udpPort, Is.EqualTo(7777));
            Assert.That(manager.autoConnectMode, Is.EqualTo(AutoConnectMode.DirectThenDiscover));
            Assert.That(manager.discoveryServiceType, Is.EqualTo("pulletnet"));
            Assert.That(manager.keepReconnectingActiveServer, Is.False,
                "通用 Prefab 不应默认无限连接旧服务器，由具体项目明确开启。");
            Assert.That(manager.activeServerReconnectDelaySeconds, Is.EqualTo(3f));
        }

        [Test]
        public void Validate_RejectsMalformedWebSocketPath()
        {
            _manager.connectionMode = ConnectionMode.WebSocketOnly;
            _manager.webSocketPath = "ws";

            Assert.That(_manager.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("start with '/'", error);
        }

        [Test]
        public void Validate_IgnoresUnusedTransportPorts()
        {
            _manager.connectionMode = ConnectionMode.UdpOnly;
            _manager.tcpPort = 0;
            _manager.webSocketPort = 0;
            _manager.webSocketPath = "";

            Assert.That(_manager.ValidateConfiguration(out string error), Is.True, error);
        }

        [Test]
        public void Validate_AllowsHostOverride()
        {
            _manager.host = "";

            Assert.That(_manager.ValidateConfiguration(out string error, "game.example.com"), Is.True, error);
        }

        [Test]
        public void Validate_RejectsInvalidMainThreadQueueLimits()
        {
            _manager.payloadQueueCapacity = 0;

            Assert.That(_manager.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("Payload queue", error);
        }

        [Test]
        public void Validate_RejectsInvalidDiscoveryAttemptCount()
        {
            _manager.discoveryMaxAttempts = 0;

            Assert.That(_manager.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("Discovery", error);
        }

        [Test]
        public void QueuedDisconnect_RemainsVisibleAfterClientIsReplaced()
        {
            var oldClient = new PulletNet.ClientSDK.NetClient(new NetClientOptions());
            FieldInfo clientField = typeof(PulletNetworkManager).GetField("_client",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo update = typeof(PulletNetworkManager).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(clientField, Is.Not.Null);
            Assert.That(update, Is.Not.Null);
            var events = new System.Collections.Generic.List<string>();
            _manager.Connected += _ => events.Add("connected");
            _manager.Disconnected += _ => events.Add("disconnected");
            clientField.SetValue(_manager, oldClient);

            _manager.HandleClientConnected(oldClient, 0,
                new ConnectedEvent(1, 1, ConnectionMode.UdpWithTcpControl));
            _manager.HandleClientDisconnected(oldClient, new DisconnectedEvent("old session ended", false));
            clientField.SetValue(_manager, null);
            update.Invoke(_manager, null);

            Assert.That(events, Is.EqualTo(new[] { "connected", "disconnected" }));
            _manager.HandleClientDisconnected(oldClient, new DisconnectedEvent("late old callback", false));
            update.Invoke(_manager, null);
            Assert.That(events, Is.EqualTo(new[] { "connected", "disconnected" }));
            ((System.IDisposable)oldClient).Dispose();
        }

    }
}
