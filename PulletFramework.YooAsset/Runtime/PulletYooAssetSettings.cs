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

        [Header("Built-in resources")]
        [Tooltip("将默认 Package 的全部构建文件复制到 StreamingAssets。WebGL/小游戏运行时优先使用内置文件，缺失或版本更新的文件仍从 CDN 获取。")]
        public bool includeDefaultPackageInStreamingAssets;
        [HideInInspector] public string builtinDefaultPackageVersion = "";

        [Header("Shader variants")]
        [Tooltip("着色器变体集合的生成目录，必须位于 Assets 下。")]
        public string shaderVariantOutputDirectory = "Assets/PulletGenerate/ShaderVariants";
        [Tooltip("着色器变体集合名称。支持 {package} 占位符，多 Package 项目建议保留该占位符。")]
        public string shaderVariantNameTemplate = "PulletShaderVariants_{package}";
        [Tooltip("构建 Package 前，根据该 Package 收集到的材质刷新着色器变体集合。")]
        public bool collectShaderVariantsBeforeBuild = true;
        [Tooltip("Prepare Package 完成下载后，渐进预热该 Package 的着色器变体。")]
        public bool warmupShaderVariantsOnPrepare = true;
        [Range(1, 256)] public int shaderVariantWarmupBatchSize = 32;

        public string ResolveShaderVariantName(string targetPackageName)
        {
            string resolvedPackageName = string.IsNullOrWhiteSpace(targetPackageName)
                ? packageName
                : targetPackageName;
            string template = string.IsNullOrWhiteSpace(shaderVariantNameTemplate)
                ? "PulletShaderVariants_{package}"
                : shaderVariantNameTemplate.Trim();
            if (template.EndsWith(".shadervariants", StringComparison.OrdinalIgnoreCase))
                template = template.Substring(0, template.Length - ".shadervariants".Length);

            string resolved = template.Replace("{package}", resolvedPackageName).Trim();
            char[] characters = resolved.ToCharArray();
            const string invalidCharacters = "<>:\"/\\|?*";
            for (int index = 0; index < characters.Length; index++)
            {
                if (characters[index] < 32 || invalidCharacters.IndexOf(characters[index]) >= 0)
                    characters[index] = '_';
            }
            resolved = new string(characters).Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(resolved)
                ? $"PulletShaderVariants_{resolvedPackageName}"
                : resolved;
        }

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
                .Replace("{platform}", PulletYooAssetPlatform.Current)
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

    }
}
