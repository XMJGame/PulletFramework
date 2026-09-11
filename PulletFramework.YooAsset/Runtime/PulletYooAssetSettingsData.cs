using UnityEngine;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>
    /// YooAsset 配置数据入口。业务代码不需要持有或加载配置资产。
    /// </summary>
    public static class PulletYooAssetSettingsData
    {
        internal const string ResourceName = "PulletYooAssetSettings";

        private static PulletYooAssetSettings s_setting;
        private static bool s_loadAttempted;

        internal static PulletYooAssetSettings Setting
        {
            get
            {
                if (!s_loadAttempted)
                {
                    s_loadAttempted = true;
                    s_setting = Resources.Load<PulletYooAssetSettings>(ResourceName);
                }
                return s_setting;
            }
        }

        /// <summary>配置资产是否已成功加载。</summary>
        public static bool IsAvailable => Setting != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Reset();
        }

        internal static void Reset()
        {
            s_setting = null;
            s_loadAttempted = false;
        }
    }
}
