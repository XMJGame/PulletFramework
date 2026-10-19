using UnityEditor;

namespace PulletFramework.NetClient.Discovery.Editor
{
    /// <summary>Discovery tuning remains serialized and editable without crowding the default Inspector.</summary>
    [CustomEditor(typeof(PulletDiscoveryConnector)), CanEditMultipleObjects]
    public sealed class PulletDiscoveryConnectorEditor : UnityEditor.Editor
    {
        private bool _showAdvanced;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            Property("autoConnectMode");
            Property("connectOnStart");
            Property("enableDiscovery");
            var discovery = serializedObject.FindProperty("enableDiscovery");
            using (new EditorGUI.DisabledScope(!discovery.hasMultipleDifferentValues && !discovery.boolValue))
            {
                Property("discoveryServiceType");
                _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "高级发现配置（广播、重试与限额）", true);
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

        private void Property(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
    }
}
