using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Tests
{
    // Editor-only acceptance fixture: enter REAL PlayMode and let Unity own
    // Awake/Start/Update/OnEnable/OnDisable/OnDestroy and scene unload. Never
    // copy this into a business project or count synthetic pause as an OS test.
    public sealed class UnityApplicationPlayModeTests
    {
        private List<GameObject> _objects;
        private List<PulletNetworkManager> _managers;
        private List<Scene> _scenes;

        private IEnumerator Play(Func<Task> body, [CallerMemberName] string scenario = null)
        {
            yield return new EnterPlayMode();
            Assert.That(Application.isPlaying, Is.True);
            _objects = new List<GameObject>(); _managers = new List<PulletNetworkManager>(); _scenes = new List<Scene>();
            Assert.That(PulletNetworkManager.Instance, Is.Null);
            int firstFrame = Time.frameCount;
            // Keep one top-level enumerator across the domain-reload instruction.
            // Do not rely on nested async enumerators being consumed during a
            // UnitySetUp/UnityTearDown PlayMode transition.
            Task operation = body(); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!operation.IsCompleted)
            {
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Application scenario did not complete.");
                yield return null;
            }
            operation.GetAwaiter().GetResult();
            var receipts = Environment.GetEnvironmentVariable("PULLETNET_U6_RECEIPTS");
            Assert.That(string.IsNullOrEmpty(receipts), Is.False, "Each actual PlayMode scenario needs an independent completion receipt.");
            File.WriteAllText(Path.Combine(receipts, scenario + ".json"), JsonUtility.ToJson(new Completion
            { scenario = scenario, completed = true, playing = Application.isPlaying, firstFrame = firstFrame, lastFrame = Time.frameCount }));
        }

        [UnityTearDown] public IEnumerator Leave()
        {
            if (!Application.isPlaying) yield break;
            var cleanup = CleanupAsync(); var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!cleanup.IsCompleted)
            {
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Scenario cleanup did not complete.");
                yield return null;
            }
            cleanup.GetAwaiter().GetResult();
            yield return null;
            yield return new ExitPlayMode();
        }

        private async Task CleanupAsync()
        {
            var errors = new List<Exception>();
            if (_managers != null) foreach (var manager in _managers)
                try { await manager.ShutdownAsync(); } catch (Exception error) { errors.Add(error); }
            if (_objects != null) foreach (var instance in _objects) if (instance != null) Object.Destroy(instance);
            if (_scenes != null) foreach (var scene in _scenes)
                if (scene.IsValid() && scene.isLoaded) await Unload(scene);
            if (errors.Count != 0) throw new AggregateException(errors);
        }

        [UnityTest] public IEnumerator InspectorRelayDisableReenableAndDestroyDoNotDuplicateOrRetainCallbacks()
            => Play(async () =>
        {
            var manager = NewManager(); var relay = manager.gameObject.AddComponent<PulletNetworkEventRelay>();
            int connected = 0, bytes = 0; var thread = Thread.CurrentThread.ManagedThreadId; bool mainThread = true;
            relay.onConnected.AddListener(reconnect => { if (!reconnect) connected++; mainThread &= Thread.CurrentThread.ManagedThreadId == thread; });
            relay.onMessageReceived.AddListener(value => { bytes++; mainThread &= Thread.CurrentThread.ManagedThreadId == thread; });
            manager.gameObject.SetActive(true); await Connect(manager); await Echo(manager, 1);
            Assert.That(connected, Is.EqualTo(1)); Assert.That(bytes, Is.EqualTo(1)); Assert.That(mainThread, Is.True);
            relay.enabled = false; await Echo(manager, 2); Assert.That(bytes, Is.EqualTo(1));
            relay.enabled = true; await Echo(manager, 3); Assert.That(bytes, Is.EqualTo(2));
            Object.Destroy(relay); await Until(() => relay == null); await Echo(manager, 4); Assert.That(bytes, Is.EqualTo(2));
            Assert.That(manager.IsConnected, Is.True, "An event listener does not own the network.");
        });

        [UnityTest] public IEnumerator ExplicitShutdownCompletesBeforeTheOwnerSceneIsUnloaded()
            => Play(async () =>
        {
            var scene = NewScene(); var manager = NewManager(scene); manager.gameObject.SetActive(true);
            await Connect(manager); await Echo(manager, 5);
            var shutdown = manager.ShutdownAsync(); await Await(shutdown); Assert.That(shutdown.IsCompleted, Is.True);
            await Unload(scene); Assert.That(manager == null, Is.True); Assert.That(PulletNetworkManager.Instance, Is.Null);
            Assert.That(manager.ShutdownAsync(), Is.SameAs(shutdown));
        });

        [UnityTest] public IEnumerator SceneUnloadInvokesRealDestroyAndJoinsOwnedDiscoveryAndConnection()
            => Play(async () =>
        {
            var scene = NewScene(); var manager = NewManager(scene); manager.gameObject.SetActive(true);
            await Connect(manager); await Echo(manager, 6);
            manager.enableDiscovery = true; manager.discoveryTimeoutSeconds = 30; manager.discoveryMaxAttempts = 1;
            var discovery = manager.DiscoverAsync(); Assert.That(manager.IsDiscovering, Is.True);
            await Unload(scene); Assert.That(manager == null, Is.True);
            await Await(manager.ShutdownAsync()); Assert.That(discovery.IsCompleted, Is.True);
            Assert.That((await discovery).Count, Is.Zero); Assert.That(PulletNetworkManager.Instance, Is.Null);
        });

        [UnityTest] public IEnumerator PersistentManagerSurvivesOriginalSceneUnloadUntilExplicitShutdown()
            => Play(async () =>
        {
            var scene = NewScene(); var manager = NewManager(scene, true); manager.gameObject.SetActive(true);
            await Connect(manager); await Echo(manager, 7); await Unload(scene);
            Assert.That(manager != null, Is.True); Assert.That(manager.IsConnected, Is.True);
            Assert.That(PulletNetworkManager.Instance, Is.SameAs(manager)); await Echo(manager, 8);
            await Await(manager.ShutdownAsync()); Assert.That(manager.IsConnected, Is.False);
        });

        [UnityTest] public IEnumerator DuplicateManagerDestroyDoesNotCloseTheOriginalConnection()
            => Play(async () =>
        {
            var owner = NewManager(); owner.gameObject.SetActive(true); await Connect(owner);
            var duplicate = NewManager(); duplicate.gameObject.SetActive(true);
            await Until(() => duplicate == null);
            Assert.That(PulletNetworkManager.Instance, Is.SameAs(owner)); await Echo(owner, 9);
            await Await(duplicate.ShutdownAsync()); Assert.That(owner.IsConnected, Is.True);
        });

        [UnityTest] public IEnumerator InjectedPauseFocusNotificationsSuspendAndResumeThroughRealFrames()
            => Play(async () =>
        {
            var manager = NewManager(); manager.gameObject.SetActive(true); await Connect(manager); await Echo(manager, 10);
            var source = BorrowSourceForObservation(manager);
            manager.gameObject.SendMessage("OnApplicationPause", true, SendMessageOptions.RequireReceiver);
            manager.gameObject.SendMessage("OnApplicationFocus", false, SendMessageOptions.RequireReceiver);
            await Until(() => source.Client.State == ConnectionState.Suspended);
            manager.gameObject.SendMessage("OnApplicationPause", false, SendMessageOptions.RequireReceiver);
            await Task.Yield(); await Task.Yield(); Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Suspended));
            manager.gameObject.SendMessage("OnApplicationFocus", true, SendMessageOptions.RequireReceiver);
            await Until(() => manager.IsConnected); await Echo(manager, 11);
            Assert.That(BorrowSourceForObservation(manager), Is.SameAs(source), "Resume does not replace the Unity source owner.");
        });

        [UnityTest] public IEnumerator RealDestroyCancelsAndJoinsAnOutstandingUdpHandshake()
            => Play(async () =>
        {
            using var passive = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var manager = NewManager(); manager.udpPort = ((IPEndPoint)passive.Client.LocalEndPoint).Port;
            manager.gameObject.SetActive(true); var connect = manager.ConnectAsync();
            await Until(() => BorrowSourceForObservation(manager)?.Client.State == ConnectionState.Handshaking);
            var source = BorrowSourceForObservation(manager);
            Object.Destroy(manager.gameObject); await Until(() => manager == null); await Await(manager.ShutdownAsync());
            Assert.That(connect.IsCompleted, Is.True); Assert.That((await connect).IsSuccess, Is.False);
            Assert.That(source.Client.State, Is.EqualTo(ConnectionState.Disposed)); Assert.That(PulletNetworkManager.Instance, Is.Null);
        });

        [UnityTest] public IEnumerator ImportedSampleCompilesAsAnExternalConsumerAndUsesAwaitablePublicApi()
            => Play(async () =>
        {
            // The sample is compiled under its own runtime asmdef in Assets, not inside
            // the package or this friend test assembly. Only serialized/public
            // sample/Manager APIs are used for its actual operations.
            var sampleType = Type.GetType("PulletFramework.NetClient.Samples.PulletNetConnectionSample, PulletFramework.NetClient.Samples", true);
            var manager = NewManager(); var sample = manager.gameObject.AddComponent(sampleType);
            var serialized = new SerializedObject(sample); serialized.FindProperty("manager").objectReferenceValue = manager;
            serialized.FindProperty("message").stringValue = "public-api-echo"; serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.gameObject.SetActive(true);
            var connect = (Task<ConnectionResult>)sampleType.GetMethod("ConnectAsync").Invoke(sample, null);
            await Await(connect); Assert.That(connect.Result.IsSuccess, Is.True, connect.Result.Message);
            var payload = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.PayloadReceived += value => payload.TrySetResult(value.Memory.ToArray());
            var send = (Task<SendResult>)sampleType.GetMethod("SendAsync").Invoke(sample, null); await Await(send);
            Assert.That(send.Result.IsSuccess, Is.True); await Await(payload.Task);
            Assert.That(System.Text.Encoding.UTF8.GetString(payload.Task.Result), Is.EqualTo("public-api-echo"));
            var shutdown = (Task)sampleType.GetMethod("ShutdownAsync").Invoke(sample, null); await Await(shutdown);
            Assert.That(manager.IsConnected, Is.False);
        });

        private PulletNetworkManager NewManager(Scene scene = default, bool persistent = false)
        {
            var config = Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS");
            Assert.That(string.IsNullOrEmpty(config), Is.False, "A controlled native echo fixture is required.");
            var ports = JsonUtility.FromJson<Ports>(File.ReadAllText(config));
            var instance = new GameObject("Isolated Unity application owner"); instance.SetActive(false); _objects.Add(instance);
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(instance, scene);
            var manager = instance.AddComponent<PulletNetworkManager>(); _managers.Add(manager);
            manager.connectOnStart = false; manager.dontDestroyOnLoad = persistent; manager.enableConnectionLogs = false;
            manager.enableDiscovery = false; manager.enableKeepAlive = false; manager.enableReconnect = false;
            manager.connectionMode = ConnectionMode.UdpOnly; manager.udpPort = ports.udp; manager.tcpPort = ports.tcp; manager.webSocketPort = ports.ws;
            return manager;
        }
        private Scene NewScene() { var scene = SceneManager.CreateScene("Network acceptance " + Guid.NewGuid().ToString("N")); _scenes.Add(scene); return scene; }
        private static async Task Connect(PulletNetworkManager manager)
        {
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ConnectedEvent> handler = _ => connected.TrySetResult(true); manager.Connected += handler;
            try
            {
                var task = manager.ConnectAsync(); await Await(task); Assert.That(task.Result.IsSuccess, Is.True, task.Result.Message);
                await Await(connected.Task);
            }
            finally { manager.Connected -= handler; }
        }
        private static async Task Echo(PulletNetworkManager manager, byte value)
        {
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ReceivedPayload> handler = payload => received.TrySetResult(payload.Memory.ToArray()); manager.PayloadReceived += handler;
            try
            {
                var send = manager.SendAsync(new[] { value }).AsTask(); await Await(send); Assert.That(send.Result.IsSuccess, Is.True, send.Result.Message);
                await Await(received.Task); Assert.That(received.Task.Result, Is.EqualTo(new[] { value }));
            }
            finally { manager.PayloadReceived -= handler; }
        }
        private static async Task Unload(Scene scene)
        { var operation = SceneManager.UnloadSceneAsync(scene); Assert.That(operation, Is.Not.Null); await Until(() => operation.isDone); }
        private static async Task Until(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition()) { Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Expected Unity/SDK observation was not reached."); await Task.Yield(); }
        }
        private static async Task Await(Task operation)
        {
            using var watchdog = new CancellationTokenSource();
            try { Assert.That(await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(10), watchdog.Token)), Is.SameAs(operation)); await operation; }
            finally { watchdog.Cancel(); }
        }
        private static HostedClient BorrowSourceForObservation(PulletNetworkManager manager)
            => (HostedClient)typeof(PulletNetworkManager).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        [Serializable] private sealed class Ports { public int udp; public int tcp; public int ws; }
        [Serializable] private sealed class Completion { public string scenario; public bool completed; public bool playing; public int firstFrame; public int lastFrame; }
    }
}
