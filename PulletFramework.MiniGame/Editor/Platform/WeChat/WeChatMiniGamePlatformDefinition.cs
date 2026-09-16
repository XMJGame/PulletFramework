using System;
using System.Linq;
using System.Text.RegularExpressions;
using PulletFramework.MiniGame.Editor;
using UnityEditor;

namespace PulletFramework.MiniGame.Platform.WeChat.Editor
{
    public sealed class WeChatMiniGamePlatformDefinition : IMiniGamePlatformDefinition,
        IMiniGamePlatformDiagnostics
    {
        public string Id => PulletPlatformIds.WeChat;
        public string DisplayName => "微信";
        public int Order => 100;

        public MiniGamePlatformSettings LoadSettings() => MiniGameBuildSettingsData.WeChat;

        public void DrawAdditionalSettings(MiniGamePlatformSettings settings)
        {
            var weChat = (WeChatPlatformSettings)settings;
            weChat.iosHighPerformancePlus = EditorGUILayout.Toggle(
                "iOS High Performance+", weChat.iosHighPerformancePlus);
            weChat.enableNativeLeaderboard = EditorGUILayout.Toggle(
                "微信好友排行榜", weChat.enableNativeLeaderboard);
            if (weChat.enableNativeLeaderboard)
                weChat.nativeLeaderboardKey = EditorGUILayout.TextField(
                    "排行榜 Key", weChat.nativeLeaderboardKey);
        }

        public bool Validate(MiniGamePlatformSettings settings, out string error)
        {
            var weChat = (WeChatPlatformSettings)settings;
            if (weChat.enableNativeLeaderboard
                && !Regex.IsMatch(weChat.nativeLeaderboardKey ?? string.Empty,
                    "^[A-Za-z0-9_.-]{1,128}$"))
            {
                error = "微信排行榜 Key 只能包含字母、数字、下划线、点和连字符，长度为 1-128。";
                return false;
            }
            error = null;
            return true;
        }

        public void DrawDiagnostics()
        {
            bool sdkAvailable = HasType("WeChatWASM.WX");
            bool bridgeAvailable = HasType("PulletFramework.MiniGame.Platform.WeChat.WeChatSdkBridge");
            if (!sdkAvailable)
            {
                EditorGUILayout.HelpBox("微信运行时 SDK 未安装。", MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox(
                bridgeAvailable
                    ? "微信运行时桥接已就绪。"
                    : "微信运行时桥接未启用。请导入 WeChat SDK Bridge 样例，应用微信平台宏，并等待脚本编译完成。",
                bridgeAvailable ? MessageType.Info : MessageType.Warning);
        }

        private static bool HasType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetType(fullName, false) != null);
        }
    }

    [InitializeOnLoad]
    internal static class WeChatBuildAdapterRegistration
    {
        static WeChatBuildAdapterRegistration()
        {
            PulletPlatformBuild.Register(new WeChatBuildAdapter());
        }
    }
}
