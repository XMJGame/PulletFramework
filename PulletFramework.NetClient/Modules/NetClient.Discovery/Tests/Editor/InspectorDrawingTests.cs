using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PulletNet.ClientSDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PulletFramework.NetClient.Discovery.Tests
{
    /// <summary>Runs the real custom OnInspectorGUI in an EditorWindow Layout/Repaint context.</summary>
    public sealed class InspectorDrawingTests
    {
        private GameObject _first, _second;
        private PulletNetworkManager _a, _b;
        private PulletDiscoveryConnector _discovery;

        [SetUp] public void Setup()
        {
            _first = new GameObject("Inspector first");
            _second = new GameObject("Inspector second");
            _a = _first.AddComponent<PulletNetworkManager>();
            _b = _second.AddComponent<PulletNetworkManager>();
            _a.connectOnStart = _b.connectOnStart = false;
            _a.dontDestroyOnLoad = _b.dontDestroyOnLoad = false;
            _a.enableConnectionLogs = _b.enableConnectionLogs = false;
            _discovery = _first.AddComponent<PulletDiscoveryConnector>();
            _discovery.connectOnStart = false;
            _discovery.enableLogs = false;
        }

        [UnityTearDown] public IEnumerator Cleanup() => UnityTaskTest.Run(async () =>
        {
            try { await _discovery.CloseAsync(); }
            finally
            {
                try { await _a.ShutdownAsync(); }
                finally
                {
                    try { await _b.ShutdownAsync(); }
                    finally { Object.DestroyImmediate(_first); Object.DestroyImmediate(_second); }
                }
            }
        });

        [UnityTest] public IEnumerator DiscoveryDrawingPreservesAddressBudgetsAndUnknownMode()
        {
            _discovery.discoveryAddress = "192.168.10.255";
            _discovery.maximumResults = 123;
            _discovery.maximumRetainedBytes = 234567;
            _discovery.autoConnectMode = (AutoConnectMode)17;
            string original = EditorJsonUtility.ToJson(_discovery);
            var editor = UnityEditor.Editor.CreateEditor(_discovery);
            try
            {
                yield return Draw(editor, false);
                Assert.That(EditorJsonUtility.ToJson(_discovery), Is.EqualTo(original));
                yield return Draw(editor, true);
                Assert.That(EditorJsonUtility.ToJson(_discovery), Is.EqualTo(original));
                yield return Draw(editor, false);
                Assert.That(EditorJsonUtility.ToJson(_discovery), Is.EqualTo(original));
            }
            finally { Object.DestroyImmediate(editor); }
        }

        [UnityTest] public IEnumerator ManagerDrawingRetainsOtherPortsAcrossTransportChangesAndUndo()
        {
            _a.udpPort = 19000; _a.tcpPort = 19001; _a.webSocketPort = 19002;
            _a.payloadQueueBytes = 123456;
            var editor = UnityEditor.Editor.CreateEditor(_a);
            try
            {
                foreach (var mode in new[] { ConnectionMode.UdpOnly, ConnectionMode.TcpOnly, ConnectionMode.WebSocketOnly })
                {
                    _a.connectionMode = mode;
                    string before = EditorJsonUtility.ToJson(_a);
                    yield return Draw(editor, true);
                    Assert.That(EditorJsonUtility.ToJson(_a), Is.EqualTo(before));
                    yield return Draw(editor, false);
                    Assert.That(EditorJsonUtility.ToJson(_a), Is.EqualTo(before));
                }
                // Edit via the same SerializedObject used by OnInspectorGUI, then draw
                // and undo. This checks Undo plumbing, not an automated dropdown click.
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                editor.serializedObject.Update();
                editor.serializedObject.FindProperty("connectionMode").intValue = (int)ConnectionMode.UdpOnly;
                editor.serializedObject.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                yield return Draw(editor, true);
                Assert.That(_a.connectionMode, Is.EqualTo(ConnectionMode.UdpOnly));
                Undo.RevertAllDownToGroup(group);
                yield return Draw(editor, true);
                Assert.That(_a.connectionMode, Is.EqualTo(ConnectionMode.WebSocketOnly));
                Assert.That(_a.udpPort, Is.EqualTo(19000));
                Assert.That(_a.tcpPort, Is.EqualTo(19001));
                Assert.That(_a.webSocketPort, Is.EqualTo(19002));
            }
            finally { Object.DestroyImmediate(editor); }
        }

        [UnityTest] public IEnumerator MixedTargetDrawingDoesNotCopyModesOrSettings()
        {
            _a.connectionMode = ConnectionMode.UdpOnly; _b.connectionMode = ConnectionMode.TcpOnly;
            _a.udpPort = 19000; _b.udpPort = 29000;
            _b.payloadQueueBytes = 234567;
            string beforeA = EditorJsonUtility.ToJson(_a), beforeB = EditorJsonUtility.ToJson(_b);
            var editor = UnityEditor.Editor.CreateEditor(new Object[] { _a, _b });
            try
            {
                yield return Draw(editor, false);
                yield return Draw(editor, true);
                Assert.That(EditorJsonUtility.ToJson(_a), Is.EqualTo(beforeA));
                Assert.That(EditorJsonUtility.ToJson(_b), Is.EqualTo(beforeB));
            }
            finally { Object.DestroyImmediate(editor); }

            var other = _second.AddComponent<PulletDiscoveryConnector>();
            other.enableLogs = false; other.connectOnStart = false;
            _discovery.autoConnectMode = AutoConnectMode.DiscoverFirst;
            other.autoConnectMode = AutoConnectMode.DirectThenDiscover;
            other.discoveryAddress = "192.168.20.255";
            beforeA = EditorJsonUtility.ToJson(_discovery); beforeB = EditorJsonUtility.ToJson(other);
            editor = UnityEditor.Editor.CreateEditor(new Object[] { _discovery, other });
            try
            {
                yield return Draw(editor, false);
                yield return Draw(editor, true);
                Assert.That(EditorJsonUtility.ToJson(_discovery), Is.EqualTo(beforeA));
                Assert.That(EditorJsonUtility.ToJson(other), Is.EqualTo(beforeB));
            }
            finally { Object.DestroyImmediate(editor); }
            yield return UnityTaskTest.Run(async () => await other.CloseAsync());
        }

        [UnityTest] public IEnumerator PrefabModeOverrideSurvivesDrawingAndUndo()
        {
            string folder = "Assets/InspectorDrawing-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            GameObject instance = null;
            UnityEditor.Editor editor = null;
            try
            {
                // Inactive temporary prefab: no Start or network operation is run.
                var template = new GameObject("Inspector prefab");
                template.SetActive(false);
                GameObject prefab;
                try
                {
                    var manager = template.AddComponent<PulletNetworkManager>();
                    manager.connectOnStart = false; manager.dontDestroyOnLoad = false;
                    template.AddComponent<PulletDiscoveryConnector>().connectOnStart = false;
                    prefab = PrefabUtility.SaveAsPrefabAsset(template, folder + "/Source.prefab");
                }
                finally { Object.DestroyImmediate(template); }
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var component = instance.GetComponent<PulletDiscoveryConnector>();
                editor = UnityEditor.Editor.CreateEditor(component);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                editor.serializedObject.Update();
                var mode = editor.serializedObject.FindProperty("autoConnectMode");
                mode.intValue = (int)AutoConnectMode.Direct;
                editor.serializedObject.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                yield return Draw(editor, true);
                editor.serializedObject.Update();
                Assert.That(editor.serializedObject.FindProperty("autoConnectMode").prefabOverride, Is.True);
                Assert.That(component.autoConnectMode, Is.EqualTo(AutoConnectMode.Direct));
                Undo.RevertAllDownToGroup(group);
                yield return Draw(editor, false);
                Assert.That(component.autoConnectMode, Is.EqualTo(AutoConnectMode.DirectThenDiscover));
                editor.serializedObject.Update();
                Assert.That(editor.serializedObject.FindProperty("autoConnectMode").prefabOverride, Is.False);
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static IEnumerator Draw(UnityEditor.Editor editor, bool advanced)
        {
            editor.GetType().GetField("_showAdvanced", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, advanced);
            var window = ScriptableObject.CreateInstance<InspectorDrawingWindow>();
            window.Inspector = editor;
            window.position = new Rect(100, 100, 720, 1000);
            try
            {
                window.ShowUtility();
                double deadline = EditorApplication.timeSinceStartup + 3;
                while (window.Layouts < 1 || window.Repaints < 2)
                {
                    if (window.Error != null) throw new InvalidOperationException("Inspector drawing failed", window.Error);
                    Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline), "Inspector did not receive Layout/Repaint");
                    window.Repaint();
                    yield return null;
                }
                if (window.Error != null) throw new InvalidOperationException("Inspector drawing failed", window.Error);
            }
            finally { window.Inspector = null; window.Close(); }
        }
    }

    public sealed class InspectorDrawingWindow : EditorWindow
    {
        public UnityEditor.Editor Inspector;
        public int Layouts, Repaints;
        public Exception Error;
        private void OnGUI()
        {
            if (Inspector == null || Error != null) return;
            try
            {
                Inspector.OnInspectorGUI();
                if (Event.current.type == EventType.Layout) Layouts++;
                if (Event.current.type == EventType.Repaint) Repaints++;
            }
            catch (Exception error) { Error = error; }
        }
    }
}
