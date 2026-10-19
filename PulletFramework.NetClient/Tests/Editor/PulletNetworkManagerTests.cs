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
            Assert.That(_manager.connectionMode, Is.EqualTo(ConnectionMode.UdpOnly));
            Assert.That(_manager.enableReconnect, Is.True);
            Assert.That(_manager.connectOnStart, Is.False);
        }

        [Test]
        public void CompactInspectorIsSelectedAndDoesNotResetAdvancedConfiguration()
        {
            _manager.payloadQueueCapacity = 321;
            _manager.payloadQueueBytes = 123456;
            _manager.tcpPort = 19001;
            string before = EditorJsonUtility.ToJson(_manager);
            var editor = UnityEditor.Editor.CreateEditor(_manager);
            try
            {
                Assert.That(editor.GetType().FullName, Is.EqualTo("PulletFramework.NetClient.Editor.PulletNetworkManagerEditor"));
                var foldout = editor.GetType().GetField("_showAdvanced", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(foldout, Is.Not.Null);
                Assert.That(foldout.GetValue(editor), Is.False);
                foldout.SetValue(editor, true);
                foldout.SetValue(editor, false);
                Assert.That(EditorJsonUtility.ToJson(_manager), Is.EqualTo(before));
                var serialized = new SerializedObject(_manager);
                serialized.FindProperty("payloadQueueCapacity").intValue = 654;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(_manager.payloadQueueCapacity, Is.EqualTo(654));
                Assert.That(_manager.payloadQueueBytes, Is.EqualTo(123456));
                Assert.That(_manager.tcpPort, Is.EqualTo(19001));
            }
            finally { Object.DestroyImmediate(editor); }
        }

        [Test]
        public void RawAssemblyDoesNotReferenceOptionalMessagingDiscoveryOrJson()
        {
            var assembly = typeof(PulletNetworkManager).Assembly;
            foreach (var reference in assembly.GetReferencedAssemblies())
                foreach (var forbidden in new[] { "Newtonsoft.Json", "PulletNet.Messaging", "PulletNet.Discovery", "PulletFramework.Messaging", "PulletFramework.Discovery", "PulletFramework.NetClient.Messaging", "PulletFramework.NetClient.Discovery" })
                    Assert.That(reference.Name, Is.Not.EqualTo(forbidden));
            foreach (var type in assembly.GetTypes())
                Assert.That(type.Namespace ?? "", Does.Not.StartWith("PulletFramework.Messaging"));
        }

        [Test]
        public void ManagerExposesRawOperationsButNotTheRetiredTypedFacade()
        {
            var manager = typeof(PulletNetworkManager);
            foreach (var method in manager.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                foreach (var retired in new[] { "ConfigureMessaging", "Subscribe", "Unsubscribe", "SendMessageAsync", "CallAsync", "CancelPendingMessages" })
                    Assert.That(method.Name, Is.Not.EqualTo(retired));
            Assert.That(manager.GetProperty("IsMessagingConfigured"), Is.Null);
            Assert.That(manager.GetEvent("MessageProtocolError"), Is.Null);
            Assert.That(manager.GetEvent("UnhandledBusinessMessage"), Is.Null);
            foreach (var required in new[] { "ConnectAsync", "DisconnectAsync", "ShutdownAsync", "SendOnChannelAsync" })
                Assert.That(manager.GetMember(required).Length, Is.GreaterThan(0));
            Assert.That(manager.GetEvent("PayloadReceived"), Is.Not.Null);
        }

        [Test]
        public void CodeEvents_AreExposedByManagerAndInspectorEventsLiveInOptionalRelay()
        {
            PulletNetworkEventRelay relay = _gameObject.AddComponent<PulletNetworkEventRelay>();
            Assert.That(relay.onConnected, Is.Not.Null);
            Assert.That(relay.onConnectFailed, Is.Not.Null);
            Assert.That(relay.onDisconnected, Is.Not.Null);
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
            Assert.That(manager.connectionMode, Is.EqualTo(ConnectionMode.UdpOnly));
            Assert.That(manager.connectOnStart, Is.False);
            Assert.That(manager.RequiresDiscoveryMigration, Is.False);
            Assert.That(manager.keepReconnectingActiveServer, Is.False,
                "通用 Prefab 不应默认无限连接旧服务器，由具体项目明确开启。");
            Assert.That(manager.activeServerReconnectDelaySeconds, Is.EqualTo(3f));
        }

        [Test]
        public void SendWithoutConnection_RejectsBothEntryPoints()
        {
            Assert.That(_manager.SendAsync(new byte[] { 1 }).GetAwaiter().GetResult().ErrorCode,
                Is.EqualTo(SendErrorCode.NotConnected));
            Assert.That(_manager.SendOnChannelAsync(new byte[] { 1 }, 7).GetAwaiter().GetResult().ErrorCode,
                Is.EqualTo(SendErrorCode.NotConnected));
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Validate_RejectsRetiredSerializedModesWithoutReinterpretingThem(int value)
        {
            _manager.connectionMode = (ConnectionMode)value;
            Assert.That(_manager.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("retired", error);
            StringAssert.Contains("separate clients", error);
            Assert.That((byte)_manager.connectionMode, Is.EqualTo(value));
        }

        [TestCase(0)]
        [TestCase(6)]
        [TestCase(255)]
        public void Validate_RejectsUnknownMode(int value)
        {
            _manager.connectionMode = (ConnectionMode)value;
            Assert.That(_manager.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("Unsupported connection mode", error);
        }

        [TestCase(ConnectionMode.UdpOnly)]
        [TestCase(ConnectionMode.TcpOnly)]
        [TestCase(ConnectionMode.WebSocketOnly)]
        public void ValidateAndClientOptions_UseExactlyTheSelectedTransport(ConnectionMode mode)
        {
            _manager.connectionMode = mode;
            _manager.tcpPort = mode == ConnectionMode.TcpOnly ? 7778 : 0;
            _manager.udpPort = mode == ConnectionMode.UdpOnly ? 7777 : 0;
            _manager.webSocketPort = mode == ConnectionMode.WebSocketOnly ? 7779 : 0;
            Assert.That(_manager.ValidateConfiguration(out string error), Is.True, error);
            MethodInfo create = typeof(PulletNetworkManager).GetMethod("CreateClientOptions", BindingFlags.Instance | BindingFlags.NonPublic);
            var options = (NetClientOptions)create.Invoke(_manager, new object[] { "127.0.0.1", _manager.tcpPort, _manager.udpPort, _manager.webSocketPort });
            Assert.That(options.Connection.ConnectionMode, Is.EqualTo(mode));
            using (var client = (System.IDisposable)new PulletNet.ClientSDK.NetClient(options)) { }
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
        public void LegacyDiscoveryAssetFailsUntilExplicitMigrationAcknowledgement()
        {
            typeof(PulletNetworkManager).GetField("_legacyDiscoveryMode", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_manager, 2);

            Assert.That(_manager.ValidateConfiguration(out string error), Is.False);
            StringAssert.Contains("Discovery", error);
            Assert.That(_manager.RequiresDiscoveryMigration, Is.True);
            _manager.ConfirmDiscoveryMigration();
            Assert.That(_manager.ValidateConfiguration(out error), Is.True, error);
        }

        [Test]
        public void OldSerializedAutoConnectModeIsRecognizedAsMigrationRequired()
        {
            const string original = "Packages/com.xmjgame.pullet-framework.netclient/Runtime/Prefabs/PulletNetworkManager.prefab";
            const string temporary = "Assets/__PulletNetLegacyDiscovery.prefab";
            Assert.That(System.IO.File.Exists(temporary), Is.False, "Never overwrite a user's asset.");
            try
            {
                string yaml = System.IO.File.ReadAllText(original).Replace("_legacyDiscoveryMode: -1", "autoConnectMode: 1");
                System.IO.File.WriteAllText(temporary, yaml);
                AssetDatabase.ImportAsset(temporary, ImportAssetOptions.ForceSynchronousImport);
                var oldAsset = AssetDatabase.LoadAssetAtPath<GameObject>(temporary).GetComponent<PulletNetworkManager>();
                Assert.That(oldAsset.RequiresDiscoveryMigration, Is.True);
                Assert.That(oldAsset.ValidateConfiguration(out var error), Is.False);
                StringAssert.Contains("migration", error);
            }
            finally { AssetDatabase.DeleteAsset(temporary); }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator LifecycleFactsAreOrderedUntilExplicitSourceRetirement() => UnityTaskTest.Run(async () =>
        {
            _manager.enableConnectionLogs = false;
            typeof(PulletNetworkManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_manager, null);
            var create = typeof(PulletNetworkManager).GetMethod("GetOrCreateClient", BindingFlags.Instance | BindingFlags.NonPublic);
            create.Invoke(_manager, new object[] { "127.0.0.1", 7778, 7777, 7779, 0L });
            var runtime = (PulletClientRuntime)typeof(PulletNetworkManager).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager);
            var sourceField = typeof(PulletNetworkManager).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic);
            var old = (HostedClient)sourceField.GetValue(_manager);
            var events = new System.Collections.Generic.List<string>();
            _manager.Connected += _ => events.Add("connected");
            _manager.Disconnected += _ => events.Add("disconnected");
            var update = typeof(PulletNetworkManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            runtime.Record(old, PulletClientLifecycleKind.Connected, new ConnectedEvent(1, 1, ConnectionMode.UdpOnly));
            runtime.Record(old, PulletClientLifecycleKind.Disconnected, new DisconnectedEvent("old session ended", false));
            update.Invoke(_manager, null);
            Assert.That(events, Is.EqualTo(new[] { "connected", "disconnected" }));
            // Explicit retirement discards ordinary unconsumed facts. A delayed
            // old handle cannot contribute facts to a new same-key source.
            runtime.Record(old, PulletClientLifecycleKind.Disconnected, new DisconnectedEvent("unconsumed at retirement", false));
            await (System.Threading.Tasks.Task)typeof(PulletNetworkManager).GetMethod("RemoveDefaultClientAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_manager, null);
            create.Invoke(_manager, new object[] { "127.0.0.1", 7778, 7777, 7779, 0L });
            var replacement = (HostedClient)sourceField.GetValue(_manager);
            Assert.That(replacement, Is.Not.SameAs(old));
            Assert.That(old.Client.State, Is.EqualTo(ConnectionState.Disposed));
            runtime.Record(old, PulletClientLifecycleKind.Disconnected, new DisconnectedEvent("late old callback", false));
            runtime.Record(replacement, PulletClientLifecycleKind.Connected, new ConnectedEvent(2, 2, ConnectionMode.UdpOnly));
            update.Invoke(_manager, null);
            Assert.That(events, Is.EqualTo(new[] { "connected", "disconnected", "connected" }));
            await _manager.ShutdownAsync();
        });

    }
}
