using UnityEngine;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>
    /// YooAsset 配置数据入口。业务代码不需要持有或加载配置资产。
    /// </summary>
    internal static class PulletYooAssetSettingsData
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
        internal static bool IsAvailable => Setting != null;

        /// <summary>应用启动时必须准备的默认资源包。</summary>
        internal static string DefaultPackageName => Setting == null
            ? "DefaultPackage"
            : Setting.packageName;

        /// <summary>默认包启动时是否下载包内全部资源。</summary>
        internal static bool DownloadAllOnStartup => Setting != null
            && Setting.downloadAllOnStartup;

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
