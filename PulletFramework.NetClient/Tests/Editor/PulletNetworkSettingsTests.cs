using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEngine;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletNetworkSettingsTests
    {
        private PulletNetworkSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<PulletNetworkSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        [Test]
        public void Defaults_AreValidAndCreateExpectedOptions()
        {
            Assert.That(_settings.Validate(out string error), Is.True, error);

            NetClientOptions options = _settings.CreateClientOptions();

            Assert.That(options.Connection.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(options.Connection.ConnectionMode, Is.EqualTo(ConnectionMode.UdpWithTcpControl));
            Assert.That(options.Reconnect.MaxReconnectAttempts, Is.EqualTo(10));
        }

        [Test]
        public void Validate_RejectsMalformedWebSocketPath()
        {
            _settings.connectionMode = ConnectionMode.WebSocketOnly;
            _settings.webSocketPath = "ws";

            Assert.That(_settings.Validate(out string error), Is.False);
            StringAssert.Contains("start with '/'", error);
        }

        [Test]
        public void CreateClientOptions_UsesHostOverride()
        {
            NetClientOptions options = _settings.CreateClientOptions("game.example.com");

            Assert.That(options.Connection.Host, Is.EqualTo("game.example.com"));
        }

        [Test]
        public void Validate_IgnoresUnusedTransportPorts()
        {
            _settings.connectionMode = ConnectionMode.UdpOnly;
            _settings.tcpPort = 0;
            _settings.webSocketPort = 0;
            _settings.webSocketPath = "";

            Assert.That(_settings.Validate(out string error), Is.True, error);
        }

        [Test]
        public void Validate_AllowsHostOverride()
        {
            _settings.host = "";

            Assert.That(_settings.Validate(out string error, "game.example.com"), Is.True, error);
        }
    }
}
