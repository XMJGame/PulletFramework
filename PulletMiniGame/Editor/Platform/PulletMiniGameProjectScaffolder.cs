using PulletFramework.Setting;
using PulletFramework.Editor;
using UnityEditor;
using UnityEngine;

namespace PulletMiniGame.Editor
{
    public static class PulletMiniGameProjectScaffolder
    {
        private static readonly string[] ProjectFolders =
        {
            "Assets/Game/Core",
            "Assets/Game/Gameplay",
            "Assets/Game/UI",
            "Assets/Game/Level",
            "Assets/Game/Progression",
            "Assets/Game/Services",
            "Assets/Platform/Shared",
            "Assets/Platform/WeChat",
            "Assets/Platform/Douyin",
            "Assets/Settings/Pullets/Resources",
            "Assets/Settings/YooAssets",
            "Assets/Settings/URP",
            "Assets/Settings/Platforms/WeChat",
            "Assets/Settings/Platforms/Douyin"
        };

        public static void InitializeProject()
        {
            foreach (string folder in ProjectFolders)
                EnsureFolder(folder);

            _ = PulletSettingsData.Setting;
            _ = PulletBuildSettingData.Setting;
            _ = MiniGameBuildSettingsData.Common;
            _ = MiniGameBuildSettingsData.WeChat;
            _ = MiniGameBuildSettingsData.Douyin;
            _ = MiniGameRuntimeSettingsEditor.GetOrCreate("wechat");
            _ = MiniGameRuntimeSettingsEditor.GetOrCreate("douyin");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PulletFramework.PLogger.EditorInfo(
                "[PulletMiniGame] Mini game project structure is ready.");
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Replace('\\', '/').Trim('/').Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
