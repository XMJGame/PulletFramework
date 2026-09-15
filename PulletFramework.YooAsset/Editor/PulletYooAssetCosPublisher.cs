using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PulletFramework.AssetPublishing.Editor;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>通过当前对象存储供应商，按发布报告顺序上传 YooAsset 版本。</summary>
    public static class PulletYooAssetCosPublisher
    {
        private const string ImmutableCacheControl = "public, max-age=31536000, immutable";
        private const string VersionCacheControl = "no-cache, max-age=0, must-revalidate";

        public static async void PublishFromMenu()
        {
            if (!EditorUtility.DisplayDialog("上传 YooAsset 资源",
                    $"将当前平台和资源版本上传到{PulletAssetPublishingService.ActiveProviderName}。"
                    + "版本文件会在最后上传，是否继续？", "上传", "取消"))
                return;

            try
            {
                string url = await PublishCurrentVersionAsync();
                EditorUtility.DisplayDialog("上传完成", $"YooAsset CDN 地址：\n{url}", "确定");
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] 资源上传失败。");
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
                bool changed = PulletAssetPublishingService.EnsureMiniGameDownloadCors();
                EditorUtility.DisplayDialog("下载跨域配置",
                    changed ? "小游戏资源下载规则已添加，并保留了桶内其他规则。" : "所需规则已经存在，无需修改。",
                    "确定");
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] 下载跨域配置失败。");
                EditorUtility.DisplayDialog("下载跨域配置失败", exception.Message, "确定");
            }
        }

        public static void ConfigureDownloadCorsBatch()
        {
            try
            {
                PulletAssetPublishingService.EnsureMiniGameDownloadCors();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] 批量跨域配置失败。");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>供命令行和 CI 调用。不要附加 -quit，任务完成后会主动退出 Unity。</summary>
        public static async void PublishCurrentVersionBatch()
        {
            try
            {
                await PublishCurrentVersionAsync();
                PLogger.EditorInfo("[PulletYooAsset] Batch publish succeeded.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] 批量发布失败。");
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
            PulletYooAssetPublishReport.Validate(
                report, packageDirectory, packageName, packageVersion);
            PulletAssetPublishingSession publishingSession =
                PulletAssetPublishingService.CreateSession();
            string appVersion = PlayerSettings.bundleVersion?.Trim();
            if (string.IsNullOrWhiteSpace(appVersion))
                throw new InvalidDataException("PlayerSettings.bundleVersion 不能为空。");

            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
            string resourceChannel = settings.resourceChannel?.Trim('/');
            if (string.IsNullOrWhiteSpace(resourceChannel))
                throw new InvalidDataException("资源兼容通道不能为空。");
            string platform = PulletYooAssetPlatform.FromBuildTarget(buildTarget);
            string remotePackagePath = CombineKey(
                "game-assets",
                platform,
                appVersion,
                resourceChannel,
                report.packageName);
            try
            {
                await UploadReportAsync(
                    publishingSession, report, packageDirectory, remotePackagePath,
                    (file, index, count) =>
                    {
                        EnsureConfigurationUnchanged(
                            settings, packageName, packageVersion,
                            buildTarget, appVersion, resourceChannel);
                        EditorUtility.DisplayProgressBar(
                            $"上传 YooAsset 到 {publishingSession.ProviderName}",
                            $"阶段 {file.uploadPhase}/3  {file.relativePath}",
                            count == 0 ? 1f : (float)index / count);
                    });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EnsureConfigurationUnchanged(settings, packageName, packageVersion,
                buildTarget, appVersion, resourceChannel);
            string hostObjectTemplate = CombineKey(
                "game-assets", "{platform}", "{appVersion}",
                "{resourceChannel}", "{package}");
            settings.defaultHostServer = publishingSession.BuildPublicUrl(hostObjectTemplate);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            PLogger.EditorInfo(
                $"[PulletYooAsset] Publish completed via {publishingSession.ProviderName}. "
                + $"Runtime URL: {settings.defaultHostServer}");
            return settings.defaultHostServer;
        }

        internal static async Task UploadReportAsync(
            PulletAssetPublishingSession publishingSession,
            PulletYooAssetPublishReport.PublishReport report,
            string packageDirectory,
            string remotePackagePath,
            Action<PulletYooAssetPublishReport.PublishFile, int, int> beforeUpload = null)
        {
            if (publishingSession == null)
                throw new ArgumentNullException(nameof(publishingSession));
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            PulletYooAssetPublishReport.Validate(
                report, packageDirectory, report.packageName, report.packageVersion);
            PulletYooAssetPublishReport.PublishFile[] files = report.files
                .OrderBy(file => file.uploadPhase)
                .ThenBy(file => file.relativePath, StringComparer.Ordinal)
                .ToArray();
            using (publishingSession.AcquirePublication(remotePackagePath))
            {
                for (int index = 0; index < files.Length; index++)
                {
                    PulletYooAssetPublishReport.PublishFile file = files[index];
                    beforeUpload?.Invoke(file, index, files.Length);
                    if (file.uploadPhase == 3)
                    {
                        PulletYooAssetPublishReport.Validate(
                            report, packageDirectory, report.packageName,
                            report.packageVersion);
                    }

                    string sourcePath = Path.Combine(report.sourceDirectory,
                        file.relativePath.Replace('/', Path.DirectorySeparatorChar));
                    string objectKey = CombineKey(remotePackagePath, file.relativePath);
                    string uploadedUrl = await publishingSession.UploadAsync(
                        objectKey, sourcePath, cacheControl: GetCacheControl(file),
                        contentType: GetContentType(file.relativePath));
                    string expectedUrl = publishingSession.BuildPublicUrl(objectKey);
                    if (!string.Equals(uploadedUrl, expectedUrl, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"上传地址与发布目标不一致：{file.relativePath}。"
                            + $"计划地址：{expectedUrl}；实际地址：{uploadedUrl}");
                    }
                }
            }
        }

        private static void EnsureConfigurationUnchanged(
            PulletYooAssetSettings settings, string packageName, string packageVersion,
            BuildTarget buildTarget, string appVersion, string resourceChannel)
        {
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget
                || !string.Equals(PlayerSettings.bundleVersion?.Trim(), appVersion,
                    StringComparison.Ordinal)
                || !string.Equals(settings.resourceChannel?.Trim('/'), resourceChannel,
                    StringComparison.Ordinal)
                || !string.Equals(PulletYooAssetSettingsEditor.GetSelectedPackageName(settings),
                    packageName, StringComparison.Ordinal)
                || !string.Equals(settings.GetLastBuildVersion(
                    buildTarget.ToString(), packageName), packageVersion,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "发布期间平台、应用版本、资源通道或当前构建包发生变化。"
                    + "任务已停止；请检查 CDN 目录后重新发布。");
        }

        private static string CombineKey(params string[] parts)
        {
            return string.Join("/", parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Replace('\\', '/').Trim('/')));
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
