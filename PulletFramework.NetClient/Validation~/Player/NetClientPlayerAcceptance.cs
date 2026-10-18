using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.NetClient;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PulletNet.Validation
{
    // Isolated public-API consumer, not a package component or an application
    // shutdown service. Unity drives every frame and every scene callback.
    public sealed class NetClientPlayerAcceptance : MonoBehaviour
    {
        private readonly List<PulletNetworkManager> _owners = new List<PulletNetworkManager>();
        private string _output;
        private Ports _ports;
        private int _thread, _echoes, _completed;
        private bool _clean;

        private async void Start()
        {
            _thread = Thread.CurrentThread.ManagedThreadId;
            _output = Environment.GetEnvironmentVariable("PULLETNET_PLAYER_RECEIPTS");
            Exception failure = null;
            try
            {
                Require(!string.IsNullOrEmpty(_output), "Receipt directory is required.");
                _ports = JsonUtility.FromJson<Ports>(File.ReadAllText(Environment.GetEnvironmentVariable("PULLETNET_U2_PORTS")));
                foreach (var mode in new[] { ConnectionMode.UdpOnly, ConnectionMode.TcpOnly, ConnectionMode.WebSocketOnly })
                {
                    var scene = SceneManager.CreateScene("Explicit owner " + mode);
                    var owner = Create(mode, scene);
                    await Connect(owner); await Echo(owner, mode + "-first");
                    var disconnected = await owner.DisconnectAsync(); Require(disconnected.IsSuccess, disconnected.Message);
                    await Connect(owner); await Echo(owner, mode + "-new-session");
                    await owner.ShutdownAsync(); Require(!owner.IsConnected, "Explicit shutdown must finish before unloading.");
                    await Unload(scene); Require(owner == null, "Scene owner was not destroyed.");
                    Complete("ReconnectAndExplicitSceneShutdown-" + mode);
                }

                var directScene = SceneManager.CreateScene("Destroy fallback");
                var direct = Create(ConnectionMode.UdpOnly, directScene);
                await Connect(direct); await Echo(direct, "destroy-fallback");
                await Unload(directScene); await direct.ShutdownAsync();
                Require(direct == null && PulletNetworkManager.Instance == null, "Destroyed owner retained singleton.");
                Complete("SceneUnloadJoinsDestroyFallback");

                var original = SceneManager.CreateScene("Persistent owner");
                var persistent = Create(ConnectionMode.UdpOnly, original, true);
                await Connect(persistent); await Echo(persistent, "before-unload");
                await Unload(original); Require(persistent != null && persistent.IsConnected, "Persistent owner lost connection.");
                await Echo(persistent, "after-unload"); await persistent.ShutdownAsync();
                Destroy(persistent.gameObject); await Until(() => persistent == null);
                Complete("PersistentOwnerSurvivesOriginalScene");

                // A passive socket makes the handshake genuinely outstanding;
                // do not add an internal crash or lifecycle hook to the library.
                using (var passive = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                {
                    var pending = Create(ConnectionMode.UdpOnly, default, false, ((IPEndPoint)passive.Client.LocalEndPoint).Port);
                    var connect = pending.ConnectAsync();
                    await Until(() => passive.Available > 0);
                    Require(!connect.IsCompleted, "Handshake was not outstanding.");
                    Destroy(pending.gameObject); await Until(() => pending == null);
                    await pending.ShutdownAsync(); await Await(connect);
                    Require(!connect.Result.IsSuccess, "Destroyed handshake unexpectedly succeeded.");
                    Complete("DestroyJoinsOutstandingHandshake");
                }

                var last = Create(ConnectionMode.TcpOnly);
                await Connect(last); await Echo(last, "explicit-application-exit");
                await last.ShutdownAsync(); Require(!last.IsConnected, "Application owner did not stop.");
                Complete("AwaitShutdownBeforeApplicationQuit");
            }
            catch (Exception error) { failure = error; Debug.LogException(error); }
            finally
            {
                // Attempt every owner even when a scenario fails. Cleanup is
                // joined BEFORE asking Unity to terminate, never in Quit hook.
                foreach (var owner in _owners)
                    try { await owner.ShutdownAsync(); }
                    catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
                _clean = failure == null;
                if (!string.IsNullOrEmpty(_output))
                    File.WriteAllText(Path.Combine(_output, "result.json"), JsonUtility.ToJson(new Result
                    { passed = _clean && _completed == 7 && _echoes == 10, scenarios = _completed, echoes = _echoes,
                        unity = Application.unityVersion, platform = Application.platform.ToString(),
                        error = failure?.ToString(), mainThread = Thread.CurrentThread.ManagedThreadId == _thread }));
                Application.Quit(_clean && _completed == 7 && _echoes == 10 ? 0 : 1);
            }
        }

        private PulletNetworkManager Create(ConnectionMode mode, Scene scene = default, bool persistent = false, int udpOverride = 0)
        {
            Require(PulletNetworkManager.Instance == null, "Previous singleton is still alive.");
            var instance = new GameObject("Player acceptance " + mode); instance.SetActive(false);
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(instance, scene);
            var owner = instance.AddComponent<PulletNetworkManager>(); _owners.Add(owner);
            owner.connectOnStart = false; owner.dontDestroyOnLoad = persistent;
            owner.enableKeepAlive = false; owner.enableReconnect = false;
            owner.enableConnectionLogs = false; owner.connectionMode = mode;
            owner.udpPort = udpOverride == 0 ? _ports.udp : udpOverride; owner.tcpPort = _ports.tcp; owner.webSocketPort = _ports.ws;
            instance.SetActive(true); return owner;
        }

        private static async Task Connect(PulletNetworkManager owner)
        { var operation = owner.ConnectAsync(); await Await(operation); Require(operation.Result.IsSuccess, operation.Result.Message); }

        private async Task Echo(PulletNetworkManager owner, string value)
        {
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ReceivedPayload> handler = payload =>
            {
                // Copy only while the public payload borrow is valid.
                if (Thread.CurrentThread.ManagedThreadId != _thread) completion.TrySetException(new Exception("Payload not on Unity thread."));
                else completion.TrySetResult(payload.Memory.ToArray());
            };
            owner.PayloadReceived += handler;
            try
            {
                var send = await owner.SendAsync(Encoding.UTF8.GetBytes(value)); Require(send.IsSuccess, send.Message);
                await Await(completion.Task);
                Require(Encoding.UTF8.GetString(completion.Task.Result) == value, "Echo bytes differ."); _echoes++;
            }
            finally { owner.PayloadReceived -= handler; }
        }

        private void Complete(string scenario)
        {
            Require(Application.isPlaying && Thread.CurrentThread.ManagedThreadId == _thread, "Scenario must finish in Player main loop.");
            File.WriteAllText(Path.Combine(_output, scenario + ".json"), JsonUtility.ToJson(new Receipt
            { scenario = scenario, completed = true, playing = true, frame = Time.frameCount })); _completed++;
        }

        private void OnApplicationQuit()
        {
            if (!string.IsNullOrEmpty(_output)) File.WriteAllText(Path.Combine(_output, "quit.json"),
                JsonUtility.ToJson(new QuitReceipt { hookObserved = true, cleanupJoinedBeforeQuit = _clean }));
        }

        private static async Task Unload(Scene scene)
        { var operation = SceneManager.UnloadSceneAsync(scene); Require(operation != null, "Scene unload was rejected."); await Until(() => operation.isDone); }
        private static async Task Until(Func<bool> condition)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!condition()) { Require(clock.Elapsed.TotalSeconds < 10, "Player observation deadline expired."); await Task.Yield(); }
        }
        private static async Task Await(Task operation)
        {
            using var stop = new CancellationTokenSource();
            try { Require(await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(10), stop.Token)) == operation, "Player operation deadline expired."); await operation; }
            finally { stop.Cancel(); }
        }
        private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        [Serializable] private sealed class Ports { public int udp, tcp, ws; }
        [Serializable] private sealed class Receipt { public string scenario; public bool completed, playing; public int frame; }
        [Serializable] private sealed class Result { public bool passed, mainThread; public int scenarios, echoes; public string unity, platform, error; }
        [Serializable] private sealed class QuitReceipt { public bool hookObserved, cleanupJoinedBeforeQuit; }
    }
}
