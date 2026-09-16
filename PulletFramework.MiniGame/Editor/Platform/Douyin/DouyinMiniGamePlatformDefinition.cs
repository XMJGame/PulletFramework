using System.Linq;
using PulletFramework.MiniGame.Editor;
using UnityEditor;

namespace PulletFramework.MiniGame.Platform.Douyin.Editor
{
    public sealed class DouyinMiniGamePlatformDefinition : IMiniGamePlatformDefinition, IMiniGamePlatformDiagnostics
    {
        public string Id => PulletPlatformIds.Douyin;
        public string DisplayName => "抖音";
        public int Order => 200;

        public MiniGamePlatformSettings LoadSettings() => MiniGameBuildSettingsData.Douyin;

        public void DrawAdditionalSettings(MiniGamePlatformSettings settings)
        {
            var douyin = (DouyinPlatformSettings)settings;
            douyin.showEngineLoadingBar = EditorGUILayout.Toggle(
                "显示引擎启动进度条", douyin.showEngineLoadingBar);
            douyin.loadingBarBackgroundColor = EditorGUILayout.ColorField(
                "进度条底色", douyin.loadingBarBackgroundColor);
            if (douyin.startupImage != null)
                EditorGUILayout.HelpBox(
                    "启动图请把标题和健康提示放在中间安全区域，并留出真实进度条的位置；常见竖屏等比铺满，宽屏完整显示。",
                    MessageType.Info);
            douyin.developerToolPath = EditorGUILayout.TextField(
                "开发者工具路径", douyin.developerToolPath);
            douyin.iosHighPerformancePlus = EditorGUILayout.Toggle(
                "iOS High Performance+", douyin.iosHighPerformancePlus);
            douyin.menuButtonStyle = (EMiniGameMenuButtonStyle)EditorGUILayout.EnumPopup(
                "胶囊按钮颜色", douyin.menuButtonStyle);
            douyin.useLegacyBuildFormat = EditorGUILayout.Toggle(
                "使用旧包体格式", douyin.useLegacyBuildFormat);
        }

        public bool Validate(MiniGamePlatformSettings settings, out string error)
        {
            var douyin = (DouyinPlatformSettings)settings;
            if (douyin.firstPackageResourceMode == EFirstPackageResourceMode.Cdn
                && douyin.useLegacyBuildFormat)
            {
                error = "抖音 Data CDN 只支持新包体格式，请关闭“使用旧包体格式”。";
                return false;
            }

            error = null;
            return true;
        }

        public void DrawDiagnostics()
        {
            bool sdkAvailable = System.AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetType("TTSDK.TT", false) != null);
            bool bridgeAvailable = System.AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetType("PulletFramework.MiniGame.Platform.Douyin.DouyinSdkBridge", false) != null);
            EditorGUILayout.HelpBox(
                !sdkAvailable ? "TTSDK 未安装。"
                    : bridgeAvailable ? "抖音运行时桥接与侧边栏服务已就绪。"
                    : "TTSDK 已安装，请导入 Douyin SDK Bridge 样例，应用抖音宏并等待编译。",
                !sdkAvailable ? MessageType.Error : bridgeAvailable ? MessageType.Info : MessageType.Warning);
        }
    }

    [InitializeOnLoad]
    internal static class DouyinBuildAdapterRegistration
    {
        static DouyinBuildAdapterRegistration()
        {
            PulletPlatformBuild.Register(new DouyinBuildToolAdapter());
        }
    }
}
