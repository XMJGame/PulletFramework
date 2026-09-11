using PulletMiniGame.Platform;
using UnityEditor;
using UnityEngine;

namespace PulletMiniGame.Editor
{
    public static class MiniGameRuntimeSettingsEditor
    {
        public static MiniGameRuntimeSettings GetOrCreate(string platformId)
        {
            string platformFolder = platformId == "wechat" ? "WeChat" : platformId == "douyin" ? "Douyin" : platformId;
            string folder = $"Assets/Settings/Platforms/{platformFolder}/Resources/PulletMiniGame";
            string path = $"{folder}/{platformId}.asset";
            var settings = AssetDatabase.LoadAssetAtPath<MiniGameRuntimeSettings>(path);
            if (settings != null) return settings;
            string current = "Assets";
            foreach (string part in folder.Substring(7).Split('/'))
            {
                if (!AssetDatabase.IsValidFolder(current + "/" + part)) AssetDatabase.CreateFolder(current, part);
                current += "/" + part;
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
    }
}
