using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.MiniGame.Platform
{
    [CreateAssetMenu(menuName = "Pullets/Mini Game/Runtime Settings")]
    public sealed class MiniGameRuntimeSettings : ScriptableObject
    {
        [Serializable]
        public sealed class RewardedPlacement
        {
            public string name;
            public string adUnitId;
        }

        public string platformId;
        public string shareTitle;
        public string shareImageUrl;
        public string shareQuery;
        public string sidebarActivityId;
        public RewardedPlacement[] rewardedAds = Array.Empty<RewardedPlacement>();

        public static MiniGameRuntimeSettings Load(string platformId)
        {
            MiniGameRuntimeSettings settings = Resources.Load<MiniGameRuntimeSettings>(
                "PulletFramework.MiniGame/" + platformId);
            return settings != null
                ? settings
                : Resources.Load<MiniGameRuntimeSettings>("PulletMiniGame/" + platformId);
        }

        public Dictionary<string, string> CreateAdMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var placement in rewardedAds ?? Array.Empty<RewardedPlacement>())
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.name))
                    throw new InvalidOperationException("Rewarded placement name is required.");
                string name = placement.name.Trim();
                if (map.ContainsKey(name))
                    throw new InvalidOperationException($"Duplicate rewarded placement: {name}");
                map.Add(name, placement.adUnitId?.Trim());
            }
            return map;
        }
    }
}
