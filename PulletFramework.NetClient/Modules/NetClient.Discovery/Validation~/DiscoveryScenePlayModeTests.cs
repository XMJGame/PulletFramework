using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PulletFramework.NetClient.Discovery.Tests
{
    // Reserved for U11's actual PlayMode pass; EditMode is not scene-unload evidence.
    public sealed class DiscoveryScenePlayModeTests
    {
        [UnityTest] public IEnumerator SceneUnloadRequestsDiscoveryCloseAndJoinsItsRealIo() => Run(async () =>
        {
            using var passive = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var scene = SceneManager.CreateScene("Optional discovery scene");
            var owner = new GameObject("Optional discovery owner");
            owner.SetActive(false); SceneManager.MoveGameObjectToScene(owner, scene);
            var manager = owner.AddComponent<PulletNetworkManager>();
            manager.dontDestroyOnLoad = false; manager.connectOnStart = false; manager.enableConnectionLogs = false;
            var connector = owner.AddComponent<PulletDiscoveryConnector>();
            connector.connectOnStart = false; connector.enableLogs = false;
            connector.discoveryAddress = "127.0.0.1";
            connector.discoveryPort = ((IPEndPoint)passive.Client.LocalEndPoint).Port;
            connector.discoveryTimeoutSeconds = 30; connector.discoveryMaxAttempts = 1;
            owner.SetActive(true);
            var sent = passive.ReceiveAsync(); var discovery = connector.DiscoverAsync();
            try
            {
                await sent;
                var unload = SceneManager.UnloadSceneAsync(scene);
                while (!unload.isDone) await Task.Yield();
                await connector.CloseAsync(); await manager.ShutdownAsync();
                Assert.That(discovery.IsCompleted, Is.True);
                try { await discovery; Assert.Fail("Destroyed discovery owner claimed successful discovery."); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                await connector.CloseAsync(); await manager.ShutdownAsync();
                if (scene.isLoaded) { var unload = SceneManager.UnloadSceneAsync(scene); while (!unload.isDone) await Task.Yield(); }
            }
        });
        private static IEnumerator Run(Func<Task> body)
        {
            var work = body(); var clock = Stopwatch.StartNew();
            while (!work.IsCompleted) { Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(10)); yield return null; }
            work.GetAwaiter().GetResult();
        }
    }
}
