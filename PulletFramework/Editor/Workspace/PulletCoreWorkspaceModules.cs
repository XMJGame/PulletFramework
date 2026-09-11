using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>通用 Player 配置。资源和小游戏配置由各自模块提供。</summary>
    public sealed class PulletPlayerWorkspaceModule : IPulletWorkspaceModule
    {
        private bool _showSigningSettings;

        public string Id => "player";
        public string DisplayName => "Player 构建";
        public string Description => "管理通用 Player 版本、目标平台和本机发布参数。";
        public int Order => 100;

        public void OnEnable() { }
        public void OnDisable() => Save();

        public void OnGUI()
        {
            DrawPlayerSettings();
            if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.Android)
            {
                GUILayout.Space(12f);
                DrawSigningSettings();
            }
            GUILayout.Space(12f);
            DrawActions();
        }

        private static void DrawPlayerSettings()
        {
            PulletBuildSetting setting = PulletBuildSettingData.Setting;
            var serialized = new SerializedObject(setting);
            serialized.Update();

            EditorGUILayout.LabelField("Player", EditorStyles.boldLabel);
            SerializedProperty buildTarget = serialized.FindProperty("buildTarget");
            EditorGUILayout.PropertyField(buildTarget, new GUIContent("目标平台"));
            EditorGUILayout.PropertyField(serialized.FindProperty("appVersion"), new GUIContent("Player 版本"));
            if ((EBuildTarget)buildTarget.enumValueIndex == EBuildTarget.Android)
                EditorGUILayout.PropertyField(serialized.FindProperty("appVersionCode"),
                    new GUIContent("Android 版本号"));

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

        private void DrawSigningSettings()
        {
            _showSigningSettings = EditorGUILayout.Foldout(
                _showSigningSettings, "Android 签名", true);
            if (!_showSigningSettings)
                return;

            EditorGUI.indentLevel++;
            PulletBuildSetting setting = PulletBuildSettingData.Setting;
            EditorGUI.BeginChangeCheck();
            setting.keystoreName = EditorGUILayout.TextField("Android Keystore", setting.keystoreName);
            setting.keystorePass = EditorGUILayout.PasswordField("Keystore 密码", setting.keystorePass);
            setting.keyaliasName = EditorGUILayout.TextField("Key Alias", setting.keyaliasName);
            setting.keyaliasPass = EditorGUILayout.PasswordField("Alias 密码", setting.keyaliasPass);
            if (EditorGUI.EndChangeCheck())
                PulletBuildSettingData.IsDirty = true;
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

        }

        private static void Save()
        {
            if (PulletBuildSettingData.IsDirty)
                PulletBuildSettingData.SaveFile();
        }
    }

}
