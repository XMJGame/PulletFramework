using System.IO;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.AssetPublishing.Editor
{
    public static class PulletAssetPublishingSettingsData
    {
        public const string AssetPath =
            "Assets/Settings/Pullets/Publishing/PulletAssetPublishingSettings.asset";
        private static PulletAssetPublishingSettings s_setting;
        private static bool s_isDirty;

        public static PulletAssetPublishingSettings Setting
        {
            get
            {
                if (s_setting == null)
                    s_setting = LoadOrCreate();
                return s_setting;
            }
        }

        public static void Save()
        {
            EditorUtility.SetDirty(Setting);
            AssetDatabase.SaveAssets();
            s_isDirty = false;
        }

        public static void MarkDirty()
        {
            EditorUtility.SetDirty(Setting);
            s_isDirty = true;
        }

        public static void SaveIfDirty()
        {
            if (s_isDirty)
                Save();
        }

        private static PulletAssetPublishingSettings LoadOrCreate()
        {
            PulletAssetPublishingSettings setting =
                AssetDatabase.LoadAssetAtPath<PulletAssetPublishingSettings>(AssetPath);
            if (setting != null)
                return setting;

            string directory = Path.GetDirectoryName(AssetPath)?.Replace('\\', '/');
            EnsureFolders(directory);
            setting = ScriptableObject.CreateInstance<PulletAssetPublishingSettings>();
            AssetDatabase.CreateAsset(setting, AssetPath);
            AssetDatabase.SaveAssets();
            return setting;
        }

        private static void EnsureFolders(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }

    /// <summary>迁移期桥接入口，旧上传后端通过反射读取新配置。</summary>
    public static class PulletAssetPublishingSettingsBridge
    {
        public static string GetValue(string name)
        {
            PulletAssetPublishingSettings setting = PulletAssetPublishingSettingsData.Setting;
            PulletAssetPublishingProfile profile =
                setting.GetOrCreateProfile(setting.activeProviderId);
            switch (name)
            {
                case "providerId": return setting.activeProviderId;
                case "accessKeyId": return profile.accessKeyId;
                case "accessKeySecret": return profile.accessKeySecret;
                case "bucket": return profile.bucket;
                case "region": return profile.region;
                case "endpoint": return profile.endpoint;
                case "publicBaseUrl": return profile.publicBaseUrl;
                case "rootFolder": return profile.rootFolder;
                default: return string.Empty;
            }
        }
    }
}
