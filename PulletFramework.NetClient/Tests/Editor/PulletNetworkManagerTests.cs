using NUnit.Framework;
using PulletNet.ClientSDK;
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
        public void InspectorEvents_ArePublicAndInitialized()
        {
            Assert.That(_manager.OnConnected, Is.Not.Null);
            Assert.That(_manager.OnConnectedFailed, Is.Not.Null);
            Assert.That(_manager.OnDisconnected, Is.Not.Null);
            Assert.That(_manager.OnDiscoveryStarted, Is.Not.Null);
            Assert.That(_manager.OnDiscoverySucceeded, Is.Not.Null);
            Assert.That(_manager.OnDiscoveryFailed, Is.Not.Null);
            Assert.That(_manager.OnDiscoveryStopped, Is.Not.Null);
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

    }
}
