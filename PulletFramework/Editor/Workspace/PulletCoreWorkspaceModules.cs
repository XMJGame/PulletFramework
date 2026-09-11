using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PulletFramework.Editor
{
    /// <summary>框架运行时的通用设置。</summary>
    public sealed class PulletRuntimeWorkspaceModule : IPulletWorkspaceModule
    {
        public string Id => "framework";
        public string DisplayName => "框架设置";
        public string Description => "管理 PulletFramework 的运行时通用行为。";
        public int Order => 50;

        public void OnEnable() { }
        public void OnDisable() => Save();

        public void OnGUI()
        {
            Setting.PulletSettings settings = Setting.PulletSettingsData.Setting;
            EditorGUI.BeginChangeCheck();
            settings.logLevel = (EPulletLogLevel)EditorGUILayout.EnumPopup(
                new GUIContent("日志等级"), settings.logLevel);
            if (EditorGUI.EndChangeCheck())
            {
                Setting.PulletSettingsData.IsDirty = true;
            }

            EditorGUILayout.HelpBox(
                "仅控制游戏运行时日志，不影响构建、上传等编辑器工具反馈。Error：仅错误；Warning：警告与错误；Info：常规日志；Debug：高频诊断；Off：关闭。",
                MessageType.Info);
        }

        private static void Save()
        {
            if (Setting.PulletSettingsData.IsDirty)
                Setting.PulletSettingsData.SaveFile();
        }
    }

    /// <summary>通用 Player 配置。资源和小游戏配置由各自模块提供。</summary>
    public sealed class PulletPlayerWorkspaceModule : IPulletWorkspaceModule
    {
        private bool _showSigningSettings;
        private bool _showBuildScenes = true;

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
            DrawBuildScenes();
            if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.Android)
            {
                GUILayout.Space(12f);
                DrawSigningSettings();
            }
            GUILayout.Space(12f);
            DrawActions();
        }

        private void DrawBuildScenes()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int enabledCount = PulletPlayerBuildService.GetEnabledScenes().Length;
            _showBuildScenes = EditorGUILayout.Foldout(
                _showBuildScenes, $"构建场景 ({enabledCount}/{scenes.Length})", true);
            if (!_showBuildScenes)
                return;

            EditorGUI.indentLevel++;
            int moveFrom = -1;
            int moveTo = -1;
            int removeIndex = -1;
            bool changed = false;
            int buildIndex = 0;

            if (scenes.Length == 0)
                EditorGUILayout.HelpBox("尚未添加构建场景。", MessageType.Warning);

            for (int index = 0; index < scenes.Length; index++)
            {
                EditorBuildSettingsScene scene = scenes[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool enabled = EditorGUILayout.Toggle(scene.enabled, GUILayout.Width(18f));
                    if (enabled != scene.enabled)
                    {
                        scene.enabled = enabled;
                        changed = true;
                    }

                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextField(
                            scene.enabled ? (buildIndex++).ToString() : "-", GUILayout.Width(30f));

                    SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                    SceneAsset selectedAsset = (SceneAsset)EditorGUILayout.ObjectField(
                        sceneAsset, typeof(SceneAsset), false);
                    if (selectedAsset != sceneAsset && selectedAsset != null)
                    {
                        scene.path = AssetDatabase.GetAssetPath(selectedAsset);
                        changed = true;
                    }

                    using (new EditorGUI.DisabledScope(index == 0))
                    {
                        if (GUILayout.Button("上移", GUILayout.Width(44f)))
                        {
                            moveFrom = index;
                            moveTo = index - 1;
                        }
                    }
                    using (new EditorGUI.DisabledScope(index == scenes.Length - 1))
                    {
                        if (GUILayout.Button("下移", GUILayout.Width(44f)))
                        {
                            moveFrom = index;
                            moveTo = index + 1;
                        }
                    }
                    if (GUILayout.Button("移除", GUILayout.Width(44f)))
                        removeIndex = index;
                }

                if (string.IsNullOrWhiteSpace(scene.path)
                    || AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) == null)
                {
                    EditorGUILayout.HelpBox(
                        $"场景文件不存在：{scene.path}", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField(scene.path, EditorStyles.miniLabel);
                }
            }

            if (moveFrom >= 0)
            {
                EditorBuildSettingsScene temporary = scenes[moveFrom];
                scenes[moveFrom] = scenes[moveTo];
                scenes[moveTo] = temporary;
                changed = true;
            }
            if (removeIndex >= 0)
            {
                var updated = new EditorBuildSettingsScene[scenes.Length - 1];
                if (removeIndex > 0)
                    System.Array.Copy(scenes, 0, updated, 0, removeIndex);
                if (removeIndex < scenes.Length - 1)
                    System.Array.Copy(scenes, removeIndex + 1, updated, removeIndex,
                        scenes.Length - removeIndex - 1);
                scenes = updated;
                changed = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("添加当前场景", GUILayout.Height(28f)))
                    changed |= AddCurrentScene(ref scenes);
                if (GUILayout.Button("移除无效场景", GUILayout.Height(28f)))
                    changed |= RemoveMissingScenes(ref scenes);
            }

            if (changed)
                EditorBuildSettings.scenes = scenes;
            EditorGUI.indentLevel--;
        }

        private static bool AddCurrentScene(ref EditorBuildSettingsScene[] scenes)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
                return false;
            if (string.IsNullOrWhiteSpace(activeScene.path))
            {
                EditorUtility.DisplayDialog("无法添加场景", "请先保存当前场景。", "确定");
                return false;
            }
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (string.Equals(scene.path, activeScene.path,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    scene.enabled = true;
                    return true;
                }
            }

            System.Array.Resize(ref scenes, scenes.Length + 1);
            scenes[scenes.Length - 1] = new EditorBuildSettingsScene(activeScene.path, true);
            return true;
        }

        private static bool RemoveMissingScenes(ref EditorBuildSettingsScene[] scenes)
        {
            int validCount = 0;
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (!string.IsNullOrWhiteSpace(scene.path)
                    && AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null)
                    validCount++;
            }
            if (validCount == scenes.Length)
                return false;

            var validScenes = new EditorBuildSettingsScene[validCount];
            int destination = 0;
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (!string.IsNullOrWhiteSpace(scene.path)
                    && AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null)
                    validScenes[destination++] = scene;
            }
            scenes = validScenes;
            return true;
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
