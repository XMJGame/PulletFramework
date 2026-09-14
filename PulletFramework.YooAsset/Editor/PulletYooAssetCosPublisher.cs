using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PulletAssetPublishing.Editor;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>按发布报告顺序将 YooAsset 版本上传到腾讯云 COS。</summary>
    public static class PulletYooAssetCosPublisher
    {
        private const string ImmutableCacheControl = "public, max-age=31536000, immutable";
        private const string VersionCacheControl = "no-cache, max-age=0, must-revalidate";

        public static async void PublishFromMenu()
        {
            if (!EditorUtility.DisplayDialog("上传 YooAsset 资源",
                    "将当前平台和资源版本上传到腾讯云 COS。版本文件会在最后上传，是否继续？", "上传", "取消"))
                return;

            try
            {
                string url = await PublishCurrentVersionAsync();
                EditorUtility.DisplayDialog("上传完成", $"YooAsset CDN 地址：\n{url}", "确定");
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] COS 上传失败。");
                EditorUtility.DisplayDialog("上传失败", exception.Message, "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void ConfigureDownloadCors()
        {
            try
            {
                bool changed = TencentCOS.EnsureMiniGameDownloadCors();
                EditorUtility.DisplayDialog("COS 跨域配置",
                    changed ? "小游戏资源下载规则已添加，并保留了桶内其他规则。" : "所需规则已经存在，无需修改。",
                    "确定");
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] COS 跨域配置失败。");
                EditorUtility.DisplayDialog("COS 跨域配置失败", exception.Message, "确定");
            }
        }

        public static void ConfigureDownloadCorsBatch()
        {
            try
            {
                TencentCOS.EnsureMiniGameDownloadCors();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] COS 批量跨域配置失败。");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>供命令行和 CI 调用。不要附加 -quit，任务完成后会主动退出 Unity。</summary>
        public static async void PublishCurrentVersionBatch()
        {
            try
            {
                await PublishCurrentVersionAsync();
                PLogger.EditorInfo("[PulletYooAsset] COS batch publish succeeded.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] COS 批量发布失败。");
                EditorApplication.Exit(1);
            }
        }

        public static async Task<string> PublishCurrentVersionAsync()
        {
            PulletYooAssetSettings settings =
                AssetDatabase.LoadAssetAtPath<PulletYooAssetSettings>(
                    PulletYooAssetSettingsEditor.DefaultAssetPath);
            if (settings == null)
                throw new InvalidDataException(
                    $"YooAsset settings not found: {PulletYooAssetSettingsEditor.DefaultAssetPath}");

            string packageVersion = PulletYooAssetSettingsEditor.RequireLastBuildVersion(settings);
            string packageName = PulletYooAssetSettingsEditor.GetSelectedPackageName(settings);
            string packageDirectory = Path.Combine(
                YooAsset.Editor.BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                packageName,
                packageVersion);
            string reportPath = PulletYooAssetPublishReport.Create(
                packageDirectory, packageName, packageVersion);
            PulletYooAssetPublishReport.PublishReport report =
                PulletYooAssetPublishReport.Load(reportPath);
            TencentCosConfiguration configuration = TencentCOS.GetConfiguration();
            configuration.Validate();
            string appVersion = PlayerSettings.bundleVersion?.Trim();
            if (string.IsNullOrWhiteSpace(appVersion))
                throw new InvalidDataException("PlayerSettings.bundleVersion 不能为空。");

            string remotePackagePath = TencentCOS.CombineKey(
                "game-assets",
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                appVersion,
                settings.resourceChannel,
                report.packageName);
            PulletYooAssetPublishReport.PublishFile[] files = report.files
                .OrderBy(file => file.uploadPhase)
                .ThenBy(file => file.relativePath, StringComparer.Ordinal)
                .ToArray();

            try
            {
                for (int index = 0; index < files.Length; index++)
                {
                    PulletYooAssetPublishReport.PublishFile file = files[index];
                    EditorUtility.DisplayProgressBar("上传 YooAsset 到腾讯云 COS",
                        $"阶段 {file.uploadPhase}/3  {file.relativePath}",
                        files.Length == 0 ? 1f : (float)index / files.Length);
                    string sourcePath = Path.Combine(report.sourceDirectory,
                        file.relativePath.Replace('/', Path.DirectorySeparatorChar));
                    string objectKey = TencentCOS.CombineKey(remotePackagePath, file.relativePath);
                    await TencentCOS.PutObjectAsync(objectKey, sourcePath, cacheControl:
                        GetCacheControl(file), contentType: GetContentType(file.relativePath));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string hostTemplate = TencentCOS.CombineKey(
                configuration.Folder, "game-assets", "{platform}", "{appVersion}",
                "{resourceChannel}", "{package}");
            settings.defaultHostServer = configuration.BaseUrl.TrimEnd('/') + "/" + hostTemplate;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            PLogger.EditorInfo(
                $"[PulletYooAsset] COS publish completed. Runtime URL: {settings.defaultHostServer}");
            return settings.defaultHostServer;
        }

        private static string GetCacheControl(PulletYooAssetPublishReport.PublishFile file)
        {
            return string.Equals(file.role, "VersionPointer", StringComparison.Ordinal)
                ? VersionCacheControl
                : ImmutableCacheControl;
        }

        private static string GetContentType(string relativePath)
        {
            switch (Path.GetExtension(relativePath).ToLowerInvariant())
            {
                case ".version":
                case ".hash":
                case ".txt":
                    return "text/plain; charset=utf-8";
                case ".json":
                case ".report":
                    return "application/json; charset=utf-8";
                case ".xml":
                    return "application/xml; charset=utf-8";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
