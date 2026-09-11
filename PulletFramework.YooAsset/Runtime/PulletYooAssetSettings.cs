using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PulletFramework.YooAssetAdapter
{
    [Serializable]
    public sealed class PulletYooAssetBuildRecord
    {
        public string platform;
        public string packageName;
        public string packageVersion;
    }

    public enum EPulletYooAssetPackageVersionMode
    {
        Manual,
        DateTime
    }

    /// <summary>YooAsset 运行模式。编辑器与真机可分别配置，避免测试配置进入发布包。</summary>
    public enum EPulletYooAssetPlayMode
    {
        EditorSimulate,
        Offline,
        Host,
        Web
    }

    /// <summary>YooAsset 的独立配置。这里的 CDN 只承载业务资源，不是小游戏首包 CDN。</summary>
    [CreateAssetMenu(fileName = "PulletYooAssetSettings", menuName = "Pullet/YooAsset Settings")]
    public sealed class PulletYooAssetSettings : ScriptableObject
    {
        [Header("Package")]
        public string packageName = "DefaultPackage";
        [HideInInspector] public string editorSelectedPackageName = "DefaultPackage";
        public EPulletYooAssetPlayMode editorPlayMode = EPulletYooAssetPlayMode.EditorSimulate;
        public EPulletYooAssetPlayMode playerPlayMode = EPulletYooAssetPlayMode.Host;
        public EPulletYooAssetPlayMode webPlayMode = EPulletYooAssetPlayMode.Web;

        [Header("Business asset CDN")]
        [Tooltip("可使用 {platform}、{appVersion}、{resourceChannel}、{package} 占位符。目录应直接包含 YooAsset 版本指针、清单和资源文件。")]
        public string defaultHostServer = "";
        public string fallbackHostServer = "";
        [FormerlySerializedAs("appVersion")]
        [Tooltip("稳定的 CDN 兼容目录，例如 v1。普通资源更新不修改它。")]
        public string resourceChannel = "v1";
        [Tooltip("手动维护语义版本，或在构建时自动生成日期版本。")]
        public EPulletYooAssetPackageVersionMode packageVersionMode =
            EPulletYooAssetPackageVersionMode.Manual;
        [Tooltip("YooAsset 清单版本。每次资源内容变化都使用新值，不影响应用商店版本。")]
        public string packageVersion = "1.0.0";
        [HideInInspector] public string lastBuildPackageVersion = "";
        [HideInInspector] public List<PulletYooAssetBuildRecord> buildRecords =
            new List<PulletYooAssetBuildRecord>();

        [Header("Download")]
        public bool downloadAllOnStartup = true;
        [Range(1, 16)] public int maximumConcurrency = 4;
        [Range(0, 10)] public int retryCount = 2;
        [Range(5, 180)] public int timeoutSeconds = 30;
        [Range(1, 100)] public int operationTimeSliceMilliseconds = 30;

        public EPulletYooAssetPlayMode GetRuntimePlayMode()
        {
#if UNITY_EDITOR
            return editorPlayMode;
#else
            return Application.platform == RuntimePlatform.WebGLPlayer ? webPlayMode : playerPlayMode;
#endif
        }

        public string GetLastBuildVersion(string targetPlatform, string targetPackageName)
        {
            PulletYooAssetBuildRecord record = buildRecords?.Find(item =>
                string.Equals(item.platform, targetPlatform, StringComparison.Ordinal)
                && string.Equals(item.packageName, targetPackageName, StringComparison.Ordinal));
            if (record != null && !string.IsNullOrWhiteSpace(record.packageVersion))
                return record.packageVersion.Trim();

            // 兼容升级前只有一个默认包版本的配置。
            if (string.Equals(targetPackageName, packageName, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(lastBuildPackageVersion))
                return lastBuildPackageVersion.Trim();
            return string.Empty;
        }

        public void RecordBuildVersion(
            string targetPlatform, string targetPackageName, string targetPackageVersion)
        {
            if (buildRecords == null)
                buildRecords = new List<PulletYooAssetBuildRecord>();
            PulletYooAssetBuildRecord record = buildRecords.Find(item =>
                string.Equals(item.platform, targetPlatform, StringComparison.Ordinal)
                && string.Equals(item.packageName, targetPackageName, StringComparison.Ordinal));
            if (record == null)
            {
                record = new PulletYooAssetBuildRecord
                {
                    platform = targetPlatform,
                    packageName = targetPackageName
                };
                buildRecords.Add(record);
            }
            record.packageVersion = targetPackageVersion;
            if (string.Equals(targetPackageName, packageName, StringComparison.Ordinal))
                lastBuildPackageVersion = targetPackageVersion;
        }

        public string ResolveDefaultHostServer() => ResolveDefaultHostServer(packageName);

        /// <summary>解析指定资源包的主 CDN 地址。</summary>
        public string ResolveDefaultHostServer(string targetPackageName) =>
            ResolveHost(defaultHostServer, targetPackageName);

        public string ResolveFallbackHostServer() => ResolveFallbackHostServer(packageName);

        /// <summary>解析指定资源包的备用 CDN 地址。</summary>
        public string ResolveFallbackHostServer(string targetPackageName)
        {
            string value = string.IsNullOrWhiteSpace(fallbackHostServer)
                ? defaultHostServer
                : fallbackHostServer;
            return ResolveHost(value, targetPackageName);
        }

        private string ResolveHost(string template, string targetPackageName)
        {
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            string resolvedPackageName = string.IsNullOrWhiteSpace(targetPackageName)
                ? packageName
                : targetPackageName;

            return template.TrimEnd('/')
                .Replace("{platform}", GetPlatformName())
                .Replace("{resourceChannel}", resourceChannel.Trim('/'))
                .Replace("{appVersion}", GetApplicationVersion())
                .Replace("{package}", resolvedPackageName.Trim('/'));
        }

        private static string GetApplicationVersion()
        {
#if UNITY_EDITOR
            return string.IsNullOrWhiteSpace(UnityEditor.PlayerSettings.bundleVersion)
                ? "1.0.0"
                : UnityEditor.PlayerSettings.bundleVersion.Trim('/');
#else
            return string.IsNullOrWhiteSpace(Application.version)
                ? "1.0.0"
                : Application.version.Trim('/');
#endif
        }

        private static string GetPlatformName()
        {
#if UNITY_EDITOR
            switch (UnityEditor.EditorUserBuildSettings.activeBuildTarget)
            {
                case UnityEditor.BuildTarget.Android: return "Android";
                case UnityEditor.BuildTarget.iOS: return "IPhone";
                case UnityEditor.BuildTarget.WebGL: return "WebGL";
                default: return "PC";
            }
#else
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "IPhone";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                default: return "PC";
            }
#endif
        }
    }
}
