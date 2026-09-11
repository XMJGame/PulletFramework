using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>按发布报告顺序将 YooAsset 版本上传到腾讯云 COS。</summary>
    public static class PulletYooAssetCosPublisher
    {
        private const string SettingsPath =
            "Assets/Settings/Pullets/YooAsset/PulletYooAssetSettings.asset";

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
                Debug.LogException(exception);
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
                Debug.LogException(exception);
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
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>供命令行和 CI 调用。不要附加 -quit，任务完成后会主动退出 Unity。</summary>
        public static async void PublishCurrentVersionBatch()
        {
            try
            {
                await PublishCurrentVersionAsync();
                Debug.Log("[PulletYooAsset] COS batch publish succeeded.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static async Task<string> PublishCurrentVersionAsync()
        {
            PulletYooAssetSettings settings =
                AssetDatabase.LoadAssetAtPath<PulletYooAssetSettings>(SettingsPath);
            if (settings == null)
                throw new InvalidDataException($"YooAsset settings not found: {SettingsPath}");

            string packageDirectory = Path.Combine(
                YooAsset.Editor.BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                settings.packageName,
                settings.appVersion);
            string reportPath = PulletYooAssetPublishReport.Create(
                packageDirectory, settings.packageName, settings.appVersion);
            PulletYooAssetPublishReport.PublishReport report =
                PulletYooAssetPublishReport.Load(reportPath);
            TencentCosConfiguration configuration = TencentCOS.GetConfiguration();
            configuration.Validate();

            string remotePackagePath = TencentCOS.CombineKey(
                "game-assets",
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                report.packageVersion,
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
                    await TencentCOS.PutObjectAsync(objectKey, sourcePath);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string hostTemplate = TencentCOS.CombineKey(
                configuration.Folder, "game-assets", "{platform}", "{appVersion}", "{package}");
            settings.defaultHostServer = configuration.BaseUrl.TrimEnd('/') + "/" + hostTemplate;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PulletYooAsset] COS publish completed. Runtime URL: {settings.defaultHostServer}");
            return settings.defaultHostServer;
        }
    }
}
