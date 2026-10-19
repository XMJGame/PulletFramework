using UnityEditor;
using UnityEngine;

namespace PulletFramework.NetClient.Discovery.Editor
{
    /// <summary>Discovery tuning remains serialized and editable without crowding the default Inspector.</summary>
    [CustomEditor(typeof(PulletDiscoveryConnector)), CanEditMultipleObjects]
    public sealed class PulletDiscoveryConnectorEditor : UnityEditor.Editor
    {
        private bool _showAdvanced;
        // Explicit labels: enumDisplayNames does not honor InspectorName consistently
        // across supported Unity versions. Values are wire-independent serialized facts.
        private static readonly GUIContent[] ModeLabels =
        {
            new GUIContent("直接连接（Direct）", "只连接Manager的固定地址，失败不启动发现。"),
            new GUIContent("先发现再连接（DiscoverFirst）", "跳过固定地址，发现候选后连接；不回退固定地址。"),
            new GUIContent("先直连，失败后发现（DirectThenDiscover）", "固定连接失败并清理后，进入发现流程。")
        };
        private static readonly int[] ModeValues =
        {
            (int)AutoConnectMode.Direct, (int)AutoConnectMode.DiscoverFirst, (int)AutoConnectMode.DirectThenDiscover
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            DrawConnectionMode();
            Property("connectOnStart");
            Property("enableDiscovery");
            var discovery = serializedObject.FindProperty("enableDiscovery");
            using (new EditorGUI.DisabledScope(!discovery.hasMultipleDifferentValues && !discovery.boolValue))
            {
                Property("discoveryServiceType");
                _showAdvanced = EditorGUILayout.Foldout(_showAdvanced,
                    new GUIContent("高级发现配置（广播、重试与限额）", "通常保留默认值；只影响发现请求的目的地、时序和资源上限，不是游戏连接或重连参数。"), true);
                if (_showAdvanced)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Property("discoveryAddress");
                        Property("discoveryPort");
                        Property("discoveryTimeoutSeconds");
                        Property("discoveryMaxAttempts");
                        Property("discoverySendIntervalSeconds");
                        Property("retryDiscoveryUntilConnected");
                        var retry = serializedObject.FindProperty("retryDiscoveryUntilConnected");
                        if (retry.hasMultipleDifferentValues || retry.boolValue) Property("discoveryRetrySeconds");
                        Property("maximumResults");
                        Property("maximumReceivedDatagrams");
                        Property("maximumRetainedBytes");
                    }
                }
            }
            Property("enableLogs");
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConnectionMode()
        {
            var mode = serializedObject.FindProperty("autoConnectMode");
            var label = new GUIContent(mode.displayName, mode.tooltip);
            var rect = EditorGUILayout.GetControlRect();
            // The property wrapper keeps prefab override and mixed-selection semantics.
            // Only an actual user change writes the serialized value; repaint must never
            // normalize an unknown value or copy one target's mode onto the others.
            EditorGUI.BeginProperty(rect, label, mode);
            bool previousMixed = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = mode.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.IntPopup(rect, label, mode.intValue, ModeLabels, ModeValues);
                if (EditorGUI.EndChangeCheck()) mode.intValue = selected;
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixed;
                EditorGUI.EndProperty();
            }
            if (!mode.hasMultipleDifferentValues && System.Array.IndexOf(ModeValues, mode.intValue) < 0)
                EditorGUILayout.HelpBox("未知的连接策略值；请显式选择模式，Inspector不会自动重置旧值。", MessageType.Warning);
        }

        private void Property(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
    }
}
