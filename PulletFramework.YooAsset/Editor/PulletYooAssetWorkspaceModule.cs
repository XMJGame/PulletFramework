using PulletFramework.Editor.Workspace;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>将 YooAsset 资源配置和常用发布动作嵌入统一工作台。</summary>
    public sealed class PulletYooAssetWorkspaceModule : IPulletWorkspaceModule
    {
        private const string OfficialSettingsPath =
            "Assets/Settings/YooAssets/Resources/YooAssetSettings.asset";
        private UnityEditor.Editor _settingsEditor;

        public string Id => "yooasset";
        public string DisplayName => "YooAsset 资源";
        public string Description => "管理运行模式、CDN、资源版本，并进入 YooAsset 官方收集与构建工具。";
        public int Order => 200;

        public void OnEnable()
        {
            PulletYooAssetSettings settings = AssetDatabase.LoadAssetAtPath<PulletYooAssetSettings>(
                PulletYooAssetSettingsEditor.DefaultAssetPath);
            if (settings != null)
                _settingsEditor = UnityEditor.Editor.CreateEditor(settings);
        }

        public void OnDisable()
        {
            if (_settingsEditor != null)
                Object.DestroyImmediate(_settingsEditor);
            _settingsEditor = null;
        }

        public void OnGUI()
        {
            if (_settingsEditor == null || _settingsEditor.target == null)
            {
                EditorGUILayout.HelpBox("尚未创建 Pullet YooAsset 配置。", MessageType.Info);
                if (GUILayout.Button("创建 YooAsset 运行配置", GUILayout.Height(32f)))
                    CreateSettingsEditor();
                return;
            }

            _settingsEditor.OnInspectorGUI();

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("高级配置", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("定位 Pullet 配置"))
                    SelectAsset(PulletYooAssetSettingsEditor.DefaultAssetPath);
                if (GUILayout.Button("定位 YooAsset 官方配置"))
                    SelectAsset(OfficialSettingsPath);
            }
            EditorGUILayout.HelpBox(
                "Pullet 配置负责运行策略与 CDN；YooAsset 官方配置负责清单目录和文件名前缀，两者职责不同。",
                MessageType.Info);
        }

        private void CreateSettingsEditor()
        {
            PulletYooAssetSettings settings = PulletYooAssetSettingsEditor.LoadOrCreate();
            _settingsEditor = UnityEditor.Editor.CreateEditor(settings);
        }

        private static void SelectAsset(string path)
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("未找到配置", path, "确定");
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
