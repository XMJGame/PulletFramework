using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>通用 Player 配置。资源和小游戏配置由各自模块提供。</summary>
    public sealed class PulletPlayerWorkspaceModule : IPulletWorkspaceModule
    {
        private bool _showLocalSettings;
        private bool _showLegacyTools;

        public string Id => "player";
        public string DisplayName => "Player 构建";
        public string Description => "管理通用 Player 版本、目标平台和本机发布参数。";
        public int Order => 100;

        public void OnEnable() { }
        public void OnDisable() => Save();

        public void OnGUI()
        {
            DrawPlayerSettings();
            GUILayout.Space(12f);
            DrawLocalSettings();
            GUILayout.Space(12f);
            DrawActions();
        }

        private static void DrawPlayerSettings()
        {
            PulletBuildSetting setting = PulletBuildSettingData.Setting;
            var serialized = new SerializedObject(setting);
            serialized.Update();

            EditorGUILayout.LabelField("Player", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("buildTarget"), new GUIContent("目标平台"));
            EditorGUILayout.PropertyField(serialized.FindProperty("appVersion"), new GUIContent("Player 版本"));
            EditorGUILayout.PropertyField(serialized.FindProperty("appVersionCode"), new GUIContent("Android 版本号"));

            int sceneCount = 0;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene != null && scene.enabled)
                    sceneCount++;
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("产品名称", PlayerSettings.productName);
                EditorGUILayout.IntField("启用场景", sceneCount);
                EditorGUILayout.EnumPopup("当前活动平台", EditorUserBuildSettings.activeBuildTarget);
            }

            if (serialized.ApplyModifiedProperties())
            {
                PulletBuildSettingData.IsDirty = true;
                PlayerSettings.bundleVersion = setting.appVersion;
            }
        }

        private void DrawLocalSettings()
        {
            _showLocalSettings = EditorGUILayout.Foldout(_showLocalSettings, "本机发布参数", true);
            if (!_showLocalSettings)
                return;

            EditorGUI.indentLevel++;
            PulletEditorSetting setting = PulletEditorSettingData.Setting;
            EditorGUI.BeginChangeCheck();
            setting.keystoreName = EditorGUILayout.TextField("Android Keystore", setting.keystoreName);
            setting.keystorePass = EditorGUILayout.PasswordField("Keystore 密码", setting.keystorePass);
            setting.keyaliasName = EditorGUILayout.TextField("Key Alias", setting.keyaliasName);
            setting.keyaliasPass = EditorGUILayout.PasswordField("Alias 密码", setting.keyaliasPass);
            setting.assetBundleCopyPath = EditorGUILayout.TextField("兼容资源输出目录", setting.assetBundleCopyPath);
            if (EditorGUI.EndChangeCheck())
                PulletEditorSettingData.IsDirty = true;
            EditorGUI.indentLevel--;
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
            PulletBuildSetting setting = PulletBuildSettingData.Setting;
            bool valid = PulletPlayerBuildService.Validate(setting, out string error);
            if (!valid)
                EditorGUILayout.HelpBox(error, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (EditorUserBuildSettings.activeBuildTarget != setting.GetBuildTarget()
                    && GUILayout.Button("切换到目标平台", GUILayout.Height(32f)))
                    PulletPlayerBuildService.SwitchActiveBuildTarget(setting);
                if (GUILayout.Button("Unity Build Settings", GUILayout.Height(32f)))
                    BuildPlayerWindow.ShowBuildPlayerWindow();
                if (GUILayout.Button("Player Settings", GUILayout.Height(32f)))
                    SettingsService.OpenProjectSettings("Project/Player");
            }

            using (new EditorGUI.DisabledScope(!valid || EditorApplication.isCompiling
                                               || EditorApplication.isUpdating))
            {
                if (GUILayout.Button("构建 Player", GUILayout.Height(36f))
                    && EditorUtility.DisplayDialog("构建 Player",
                        $"目标平台：{setting.GetBuildTarget()}\n输出：{PulletPlayerBuildService.GetOutputPath(setting)}",
                        "开始构建", "取消"))
                {
                    Save();
                    PulletPlayerBuildService.Build(setting);
                }
            }

            _showLegacyTools = EditorGUILayout.Foldout(_showLegacyTools, "兼容工具", true);
            if (_showLegacyTools)
            {
                EditorGUILayout.HelpBox("旧窗口代码仍被保留，仅用于迁移期核对功能。", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("旧版构建窗口"))
                    {
                        if (!PulletEditorAssetUtility.InvokeStatic(
                                "PulletFramework.Editor.PulletBuildWindow, PulletFramework.YooAsset.Editor",
                                "OpenWindow"))
                            EditorUtility.DisplayDialog("模块未安装", "旧版构建窗口属于 YooAsset 模块。", "确定");
                    }
                    if (GUILayout.Button("旧版编辑器设置"))
                        PulletEditorWindow.OpenWindow();
                    if (GUILayout.Button("旧版框架设置"))
                        PulletSettingWindow.OpenWindow();
                }
            }
        }

        private static void Save()
        {
            if (PulletBuildSettingData.IsDirty)
                PulletBuildSettingData.SaveFile();
            if (PulletEditorSettingData.IsDirty)
                PulletEditorSettingData.SaveFile();
        }
    }

}
