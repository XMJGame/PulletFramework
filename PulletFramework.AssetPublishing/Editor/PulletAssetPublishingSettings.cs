using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.AssetPublishing.Editor
{
    /// <summary>资源发布供应商配置。仅供 Unity 编辑器使用，不放入 Resources。</summary>
    [CreateAssetMenu(fileName = "PulletAssetPublishingSettings",
        menuName = "Pullet/Asset Publishing Settings")]
    public sealed class PulletAssetPublishingSettings : ScriptableObject
    {
        public string activeProviderId = "tencent-cos";
        public List<PulletAssetPublishingProfile> profiles = new List<PulletAssetPublishingProfile>();

        public PulletAssetPublishingProfile GetOrCreateProfile(string providerId)
        {
            if (profiles == null)
                profiles = new List<PulletAssetPublishingProfile>();
            PulletAssetPublishingProfile profile = profiles.Find(item => item.providerId == providerId);
            if (profile != null)
                return profile;

            profile = new PulletAssetPublishingProfile { providerId = providerId };
            profiles.Add(profile);
            return profile;
        }
    }

    /// <summary>单个对象存储供应商的编辑器发布配置。</summary>
    [Serializable]
    public sealed class PulletAssetPublishingProfile
    {
        public string providerId = "";
        public string accessKeyId = "";
        public string accessKeySecret = "";
        public string bucket = "";
        public string region = "ap-guangzhou";
        public string endpoint = "";
        public string publicBaseUrl = "";
        public string rootFolder = "";

        internal PulletAssetPublishingProfile Clone()
        {
            return new PulletAssetPublishingProfile
            {
                providerId = providerId,
                accessKeyId = accessKeyId,
                accessKeySecret = accessKeySecret,
                bucket = bucket,
                region = region,
                endpoint = endpoint,
                publicBaseUrl = publicBaseUrl,
                rootFolder = rootFolder
            };
        }
    }
}
