using UnityEngine;
using PulletFramework.Editor;

namespace PulletFramework.MiniGame.Editor
{
    public enum EMiniGameBuildEnvironment
    {
        Development,
        Test,
        Release
    }

    public enum EMiniGameOrientation
    {
        Portrait,
        Landscape
    }

    public enum EFirstPackageResourceMode
    {
        Package,
        Cdn
    }

    public enum EMiniGameMenuButtonStyle
    {
        Light,
        Dark
    }

    public sealed class MiniGameBuildSettings : ScriptableObject
    {
        public string selectedPlatformId = "wechat";
        public EMiniGameBuildEnvironment environment = EMiniGameBuildEnvironment.Development;
        public string version = "1.0.0";
        public string versionDescription = "Development build";
        public bool developmentBuild = true;
        public bool cleanOutput = true;
    }

    public static class MiniGameBuildSettingsData
    {
        private static MiniGameBuildSettings _common;
        private static WeChatPlatformSettings _weChat;
        private static DouyinPlatformSettings _douyin;

        public static MiniGameBuildSettings Common => _common ??=
            SettingLoader.LoadSettingData<MiniGameBuildSettings>("Platforms");

        public static WeChatPlatformSettings WeChat => _weChat ??=
            SettingLoader.LoadSettingData<WeChatPlatformSettings>("Platforms/WeChat");

        public static DouyinPlatformSettings Douyin => _douyin ??=
            SettingLoader.LoadSettingData<DouyinPlatformSettings>("Platforms/Douyin");

        public static void Save(MiniGamePlatformSettings platform = null)
        {
            UnityEditor.EditorUtility.SetDirty(Common);
            if (platform != null)
                UnityEditor.EditorUtility.SetDirty(platform);
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }
}
