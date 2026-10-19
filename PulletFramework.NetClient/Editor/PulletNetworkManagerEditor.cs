using UnityEditor;
using UnityEngine;

namespace PulletFramework.NetClient.Editor
{
    /// <summary>Presentation only: preserve the component's serialized fields and defaults.</summary>
    [CustomEditor(typeof(PulletNetworkManager)), CanEditMultipleObjects]
    public sealed class PulletNetworkManagerEditor : UnityEditor.Editor
    {
        private bool _showAdvanced;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            Property("connectionMode");
            Property("host");
            var mode = serializedObject.FindProperty("connectionMode");
            // Mixed selections retain all transport fields so multi-editing never
            // silently applies one selected object's transport to the others.
            if (mode.hasMultipleDifferentValues || mode.intValue == 1) Property("udpPort");
            if (mode.hasMultipleDifferentValues || mode.intValue == 4) Property("tcpPort");
            if (mode.hasMultipleDifferentValues || mode.intValue == 5)
            {
                Property("webSocketPort");
                Property("webSocketPath");
                Property("webSocketUseTls");
            }
            if (!mode.hasMultipleDifferentValues && mode.intValue != 1 && mode.intValue != 4 && mode.intValue != 5)
                EditorGUILayout.HelpBox("请选择 UdpOnly、TcpOnly 或 WebSocketOnly；旧组合模式不会自动降级。", MessageType.Warning);

            Property("connectOnStart");
            Property("dontDestroyOnLoad");
            Property("reconnectOnApplicationResume");
            Property("enableKeepAlive");
            Property("enableReconnect");
            if (EnabledOrMixed("enableReconnect")) Property("maxReconnectAttempts");
            Property("keepReconnectingActiveServer");
            if (EnabledOrMixed("keepReconnectingActiveServer")) Property("activeServerReconnectDelaySeconds");
            Property("enableConnectionLogs");

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "高级配置（主线程交付预算）", true);
            if (_showAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Property("payloadQueueCapacity");
                    Property("maxPayloadCallbacksPerFrame");
                    Property("payloadQueueBytes");
                    Property("maxPayloadBytesPerFrame");
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private bool EnabledOrMixed(string name)
        {
            var value = serializedObject.FindProperty(name);
            return value.hasMultipleDifferentValues || value.boolValue;
        }

        private void Property(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
    }
}
