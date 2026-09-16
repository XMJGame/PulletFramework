using PulletFramework.MiniGame.Platform;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    public static class MiniGameRuntimeSettingsEditor
    {
        public static MiniGameRuntimeSettings GetOrCreate(string platformId)
        {
            string platformFolder = platformId == "wechat" ? "WeChat" : platformId == "douyin" ? "Douyin" : platformId;
            string folder = $"Assets/Settings/Platforms/{platformFolder}/Resources/PulletFramework.MiniGame";
            string path = $"{folder}/{platformId}.asset";
            var settings = AssetDatabase.LoadAssetAtPath<MiniGameRuntimeSettings>(path);
            if (settings != null) return settings;
            EnsureFolder(folder);

            string legacyPath =
                $"Assets/Settings/Platforms/{platformFolder}/Resources/PulletMiniGame/{platformId}.asset";
            settings = AssetDatabase.LoadAssetAtPath<MiniGameRuntimeSettings>(legacyPath);
            if (settings != null)
            {
                string error = AssetDatabase.MoveAsset(legacyPath, path);
                if (!string.IsNullOrEmpty(error))
                    throw new System.InvalidOperationException(
                        $"小游戏平台配置迁移失败：{legacyPath} -> {path}\n{error}");
                AssetDatabase.SaveAssets();
                return AssetDatabase.LoadAssetAtPath<MiniGameRuntimeSettings>(path);
            }

            settings = ScriptableObject.CreateInstance<MiniGameRuntimeSettings>();
            settings.platformId = platformId;
            settings.rewardedAds = new[]
            {
                new MiniGameRuntimeSettings.RewardedPlacement { name = "revive" },
                new MiniGameRuntimeSettings.RewardedPlacement { name = "bonus_reward" }
            };
            AssetDatabase.CreateAsset(settings, path);
            return settings;
        }

        private static void EnsureFolder(string folder)
        {
            string current = "Assets";
            foreach (string part in folder.Substring(7).Split('/'))
            {
                if (!AssetDatabase.IsValidFolder(current + "/" + part)) AssetDatabase.CreateFolder(current, part);
                current += "/" + part;
            }
        }
    }
}
