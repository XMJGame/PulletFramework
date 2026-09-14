using System;
using System.Collections.Generic;
using System.Linq;
using PulletMiniGame.Platform;
using UnityEditor;
using UnityEngine;

namespace PulletMiniGame.Editor
{
    public sealed class PulletMiniGameBuildWindow : EditorWindow
    {
        private static readonly string[] EnvironmentLabels = { "开发", "测试", "发布" };
        private Vector2 _scrollPosition;

        public static void Open()
        {
            var window = GetWindow<PulletMiniGameBuildWindow>("小游戏发布");
            window.minSize = new Vector2(560f, 620f);
        }

        private void OnGUI()
        {
            Draw(ref _scrollPosition, true);
        }

        internal static void Draw(ref Vector2 scrollPosition, bool useScroll)
        {
            MiniGameBuildSettings common = MiniGameBuildSettingsData.Common;
            IReadOnlyList<IMiniGamePlatformDefinition> definitions = MiniGamePlatformRegistry.All;
            if (definitions.Count == 0)
            {
                EditorGUILayout.HelpBox("没有发现小游戏平台定义。", MessageType.Warning);
                return;
            }

            if (useScroll)
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            if (useScroll)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("小游戏发布", EditorStyles.boldLabel);
                EditorGUILayout.Space(6f);
            }

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(common, "Edit mini game build settings");

            int platformIndex = FindPlatformIndex(definitions, common.selectedPlatformId);
            string[] platformLabels = definitions.Select(item => item.DisplayName).ToArray();
            platformIndex = GUILayout.Toolbar(platformIndex, platformLabels, GUILayout.Height(28f));
            IMiniGamePlatformDefinition definition = definitions[platformIndex];
            common.selectedPlatformId = definition.Id;
            MiniGamePlatformSettings platform = definition.LoadSettings();
            Undo.RecordObject(platform, "Edit mini game platform settings");

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("构建环境", EditorStyles.boldLabel);
            int environmentIndex = GUILayout.Toolbar((int)common.environment, EnvironmentLabels, GUILayout.Height(24f));
            common.environment = (EMiniGameBuildEnvironment)environmentIndex;
            common.developmentBuild = common.environment != EMiniGameBuildEnvironment.Release;

            EditorGUILayout.Space(12f);
            DrawCommonSettings(common);

            EditorGUILayout.Space(12f);
            DrawPlatformSettings(definition, platform);

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("运行参数", EditorStyles.boldLabel);
            var runtimeSettings = MiniGameRuntimeSettingsEditor.GetOrCreate(definition.Id);
            var runtimeObject = new SerializedObject(runtimeSettings);
            runtimeObject.Update();
            EditorGUILayout.PropertyField(runtimeObject.FindProperty("shareTitle"), new GUIContent("默认分享标题"));
            EditorGUILayout.PropertyField(runtimeObject.FindProperty("shareImageUrl"), new GUIContent("默认分享图片 URL"));
            EditorGUILayout.PropertyField(runtimeObject.FindProperty("shareQuery"), new GUIContent("默认分享参数"));
            if (definition.Id == PulletPlatformIds.Douyin)
                EditorGUILayout.PropertyField(runtimeObject.FindProperty("sidebarActivityId"), new GUIContent("侧边栏活动 ID（可选）"));
            EditorGUILayout.PropertyField(runtimeObject.FindProperty("rewardedAds"), new GUIContent("激励广告位映射"), true);
            runtimeObject.ApplyModifiedProperties();

            DrawRuntimeStatus(runtimeSettings);

            if (EditorGUI.EndChangeCheck())
                MiniGameBuildSettingsData.Save(platform);

            EditorGUILayout.Space(12f);
            DrawStatus(common, definition, platform);
            EditorGUILayout.Space(12f);
            DrawActions(common, definition, platform, runtimeSettings);
            if (useScroll)
                EditorGUILayout.EndScrollView();
        }

        private static int FindPlatformIndex(
            IReadOnlyList<IMiniGamePlatformDefinition> definitions,
            string selectedPlatformId)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                if (string.Equals(
                    definitions[index].Id, selectedPlatformId, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return 0;
        }

        private static void DrawCommonSettings(MiniGameBuildSettings settings)
        {
            EditorGUILayout.LabelField("公共参数", EditorStyles.boldLabel);
            settings.version = EditorGUILayout.TextField("版本", settings.version);
            settings.versionDescription = EditorGUILayout.TextField("版本说明", settings.versionDescription);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle("Development Build", settings.developmentBuild);
            settings.cleanOutput = EditorGUILayout.Toggle("构建前清理输出", settings.cleanOutput);
        }

        private static void DrawPlatformSettings(
            IMiniGamePlatformDefinition definition,
            MiniGamePlatformSettings settings)
        {
            EditorGUILayout.LabelField($"{definition.DisplayName}参数", EditorStyles.boldLabel);
            settings.appId = EditorGUILayout.TextField("App ID", settings.appId);
            settings.gameName = EditorGUILayout.TextField("游戏名称", settings.gameName);
            settings.outputPath = EditorGUILayout.TextField("输出目录", settings.outputPath);
            settings.orientation = (EMiniGameOrientation)EditorGUILayout.EnumPopup("屏幕方向", settings.orientation);
            settings.startupImage = (Texture2D)EditorGUILayout.ObjectField(
                "启动背景图", settings.startupImage, typeof(Texture2D), false);
            settings.showDefaultUnityLoadingLogo = EditorGUILayout.Toggle(
                "显示 Unity 加载图标", settings.showDefaultUnityLoadingLogo);
            settings.firstPackageResourceMode = (EFirstPackageResourceMode)EditorGUILayout.EnumPopup(
                "首包资源", settings.firstPackageResourceMode);
            if (settings.firstPackageResourceMode == EFirstPackageResourceMode.Cdn)
                settings.cdnUrl = EditorGUILayout.TextField("CDN 地址", settings.cdnUrl);
            settings.initialMemoryMb = Mathf.Max(64, EditorGUILayout.IntField("初始内存 (MB)", settings.initialMemoryMb));
            definition.DrawAdditionalSettings(settings);
        }

        private static void DrawStatus(
            MiniGameBuildSettings common,
            IMiniGamePlatformDefinition definition,
            MiniGamePlatformSettings platform)
        {
            bool symbolsApplied = PulletPlatformSymbols.IsApplied(
                common.selectedPlatformId, common.environment);
            EditorGUILayout.HelpBox(
                symbolsApplied ? "平台宏已应用。" : "配置已修改，点击“应用平台”更新 WebGL 宏。",
                symbolsApplied ? MessageType.Info : MessageType.Warning);

            if (!PulletPlatformBuild.TryGet(common.selectedPlatformId, out IPlatformBuildAdapter adapter))
            {
                EditorGUILayout.HelpBox(
                    "尚未检测到对应平台的转换 SDK/导出适配器。当前可以编辑配置和开发业务，安装 SDK 后才可导出。",
                    MessageType.Warning);
                return;
            }

            bool available = adapter.IsAvailable(out string reason);
            EditorGUILayout.HelpBox(
                available ? $"SDK 已就绪：{adapter.DisplayName}" : $"SDK 不可用：{reason}",
                available ? MessageType.Info : MessageType.Error);

            if (definition is IMiniGamePlatformDiagnostics diagnostics)
                diagnostics.DrawDiagnostics();
        }

        private static void DrawRuntimeStatus(MiniGameRuntimeSettings settings)
        {
            MiniGameRuntimeSettings.RewardedPlacement[] placements = settings.rewardedAds
                ?? Array.Empty<MiniGameRuntimeSettings.RewardedPlacement>();
            int missingIds = placements.Count(item => item != null
                && !string.IsNullOrWhiteSpace(item.name)
                && string.IsNullOrWhiteSpace(item.adUnitId));

            if (missingIds > 0)
            {
                EditorGUILayout.HelpBox(
                    $"有 {missingIds} 个激励广告业务位尚未填写平台广告 ID；对应广告请求会失败。",
                    MessageType.Warning);
            }

            if (string.IsNullOrWhiteSpace(settings.shareTitle))
            {
                EditorGUILayout.HelpBox(
                    "默认分享标题为空。业务传入分享标题时不受影响。",
                    MessageType.Info);
            }
        }

        private static void DrawActions(
            MiniGameBuildSettings common,
            IMiniGamePlatformDefinition definition,
            MiniGamePlatformSettings platform,
            MiniGameRuntimeSettings runtimeSettings)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("应用平台", GUILayout.Height(34f)))
            {
                MiniGameBuildSettingsData.Save(platform);
                PulletPlatformSymbols.Apply(common.selectedPlatformId, common.environment);
            }

            bool valid = Validate(common, definition, platform, runtimeSettings, out string validationError);
            IPlatformBuildAdapter adapter = null;
            bool canExport = valid
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating
                && PulletPlatformSymbols.IsApplied(common.selectedPlatformId, common.environment)
                && PulletPlatformBuild.TryGet(common.selectedPlatformId, out adapter)
                && adapter.IsAvailable(out _);
            using (new EditorGUI.DisabledScope(!canExport))
            {
                string actionLabel = adapter?.ActionLabel ?? "导出小游戏";
                if (GUILayout.Button(actionLabel, GUILayout.Height(34f)))
                {
                    try
                    {
                        MiniGameBuildSettingsData.Save(platform);
                        PlayerSettings.bundleVersion = common.version;
                        PulletPlatformBuild.Export(common.selectedPlatformId, new PlatformBuildContext(
                            platform.outputPath, common.developmentBuild, common.cleanOutput, platform));
                    }
                    catch (Exception exception)
                    {
                        ReportBuildFailure(definition.DisplayName, platform.outputPath, exception);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!valid)
                EditorGUILayout.HelpBox(validationError, MessageType.Error);
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                EditorGUILayout.HelpBox("正在编译或导入资源，请完成后再导出。", MessageType.Info);
        }

        private static void ReportBuildFailure(
            string platformName, string outputPath, Exception exception)
        {
            Exception rootCause = exception;
            while (rootCause.InnerException != null)
                rootCause = rootCause.InnerException;

            string message = $"平台：{platformName}\n输出目录：{outputPath}\n\n原因：{exception.Message}";
            if (!ReferenceEquals(rootCause, exception)
                && !string.Equals(rootCause.Message, exception.Message, StringComparison.Ordinal))
                message += $"\n\n系统原因：{rootCause.Message}";

            PulletFramework.PLogger.EditorException(
                exception, $"[PulletMiniGame] {platformName}小游戏构建失败。\n{message}");
            EditorUtility.DisplayDialog($"{platformName}小游戏构建失败", message, "确定");
        }

        private static bool Validate(
            MiniGameBuildSettings common,
            IMiniGamePlatformDefinition definition,
            MiniGamePlatformSettings platform,
            MiniGameRuntimeSettings runtimeSettings,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(common.version))
                error = "请填写版本号。";
            else if (string.IsNullOrWhiteSpace(platform.appId))
                error = "请填写当前平台的 App ID。";
            else if (string.IsNullOrWhiteSpace(platform.gameName))
                error = "请填写游戏名称。";
            else if (string.IsNullOrWhiteSpace(platform.outputPath))
                error = "请填写输出目录。";
            else if (platform.firstPackageResourceMode == EFirstPackageResourceMode.Cdn
                     && string.IsNullOrWhiteSpace(platform.cdnUrl))
                error = "首包资源选择 CDN 时必须填写 CDN 地址。";
            else if (!definition.Validate(platform, out error))
                return false;
            else if (!ValidateRuntimeSettings(runtimeSettings, out error))
                return false;
            else
            {
                error = null;
                return true;
            }

            return false;
        }

        private static bool ValidateRuntimeSettings(MiniGameRuntimeSettings settings, out string error)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MiniGameRuntimeSettings.RewardedPlacement placement in settings.rewardedAds
                     ?? Array.Empty<MiniGameRuntimeSettings.RewardedPlacement>())
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.name))
                {
                    error = "激励广告业务位名称不能为空。";
                    return false;
                }

                string name = placement.name.Trim();
                if (!names.Add(name))
                {
                    error = $"激励广告业务位名称重复：{name}";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
