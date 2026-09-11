using System;
using System.IO;
using System.Linq;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace PulletFramework.Editor
{
    /// <summary>使用当前 YooAsset 官方构建参数，构建 Pullet 配置指定的资源版本。</summary>
    public static class PulletYooAssetPackageBuilder
    {
        public static void BuildFromWorkspace()
        {
            try
            {
                PulletYooAssetSettings settings = PulletYooAssetSettingsEditor.LoadOrCreate();
                string packageName = PulletYooAssetSettingsEditor.GetSelectedPackageName(settings);
                string pipelineName = BundleBuilderSetting.GetPackageBuildPipeline(packageName);
                string summary = $"资源包：{packageName}\n" +
                    $"平台：{EditorUserBuildSettings.activeBuildTarget}\n" +
                    $"应用版本：{PlayerSettings.bundleVersion}\n" +
                    $"兼容通道：{settings.resourceChannel}\n" +
                    $"版本模式：{GetVersionModeName(settings.packageVersionMode)}\n" +
                    $"资源包版本：{PreviewBuildVersion(settings)}\n" +
                    $"管线：{pipelineName}\n\n是否开始构建？";
                if (!EditorUtility.DisplayDialog("构建 YooAsset 当前版本", summary, "构建", "取消"))
                    return;

                string output = BuildCurrentVersion(settings);
                string report = PulletYooAssetPublishReport.Create(
                    output, packageName, GetLastBuildVersion(settings));
                PLogger.EditorInfo($"[PulletYooAsset] Package build completed: {output}\n" +
                    $"[PulletYooAsset] Publish report: {report}");
                EditorUtility.RevealInFinder(output);
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletYooAsset] Package 构建失败。");
                EditorUtility.DisplayDialog("YooAsset 构建失败", exception.Message, "确定");
            }
        }

        public static string BuildCurrentVersion(PulletYooAssetSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            string packageName = PulletYooAssetSettingsEditor.GetSelectedPackageName(settings);
            if (string.IsNullOrWhiteSpace(packageName))
                throw new BuildFailedException("请先选择要构建的资源包。");
            if (string.IsNullOrWhiteSpace(settings.resourceChannel))
                throw new BuildFailedException("请先填写资源兼容通道。");
            if (settings.packageVersionMode == EPulletYooAssetPackageVersionMode.Manual
                && string.IsNullOrWhiteSpace(settings.packageVersion))
                throw new BuildFailedException("请先填写资源包版本。");

            string pipelineName = BundleBuilderSetting.GetPackageBuildPipeline(packageName);
            if (pipelineName != EBuildPipeline.ScriptableBuildPipeline.ToString())
            {
                throw new BuildFailedException(
                    $"当前一键构建仅支持 {EBuildPipeline.ScriptableBuildPipeline}，" +
                    $"检测到 {pipelineName}。请在资源构建器中切换管线，或使用官方高级构建界面。");
            }

            string packageVersion = CreateBuildVersion(settings);
            ScriptableBuildParameters parameters = CreateParameters(settings, pipelineName, packageVersion);
            string output = parameters.GetPackageOutputDirectory();
            if (Directory.Exists(output))
            {
                throw new BuildFailedException(
                    $"资源版本目录已经存在：{output}\n" +
                    "CDN 版本保持不可变，请递增资源版本后重新构建。");
            }

            AssetDatabase.SaveAssets();
            var pipeline = new ScriptableBuildPipeline();
            BuildResult result = pipeline.Run(parameters, true);
            if (!result.Success)
                throw new BuildFailedException(result.ErrorInfo);
            settings.RecordBuildVersion(
                EditorUserBuildSettings.activeBuildTarget.ToString(), packageName, packageVersion);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return result.OutputPackageDirectory;
        }

        private static ScriptableBuildParameters CreateParameters(
            PulletYooAssetSettings settings, string pipelineName, string packageVersion)
        {
            string packageName = PulletYooAssetSettingsEditor.GetSelectedPackageName(settings);
            string shaderBundleName = DefaultBundlePackRule.CreateShadersPackRuleResult()
                .GetBundleName(packageName, BundleCollectorSettingData.Setting.UniqueBundleName);

            return new ScriptableBuildParameters
            {
                BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = pipelineName,
                BuildBundleType = (int)EBundleType.AssetBundle,
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = packageName,
                PackageVersion = packageVersion,
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = BundleBuilderSetting.GetPackageFileNameStyle(packageName, pipelineName),
                BundledCopyOption = BundleBuilderSetting.GetPackageBundledCopyOption(packageName, pipelineName),
                BundledCopyParams = BundleBuilderSetting.GetPackageBundledCopyParams(packageName, pipelineName),
                CompressOption = BundleBuilderSetting.GetPackageCompressOption(packageName, pipelineName),
                ClearBuildCacheFiles = BundleBuilderSetting.GetPackageClearBuildCache(packageName, pipelineName),
                UseAssetDependencyDB = BundleBuilderSetting.GetPackageUseAssetDependencyDB(packageName, pipelineName),
                BundleEncryptor = CreateExtension<IBundleEncryptor>(
                    BundleBuilderSetting.GetPackageBundleEncryptorClassName(packageName, pipelineName)),
                ManifestEncryptor = CreateExtension<IManifestEncryptor>(
                    BundleBuilderSetting.GetPackageManifestEncryptorClassName(packageName, pipelineName)),
                ManifestDecryptor = CreateExtension<IManifestDecryptor>(
                    BundleBuilderSetting.GetPackageManifestDecryptorClassName(packageName, pipelineName)),
                BuiltinShadersBundleName = shaderBundleName
            };
        }

        private static T CreateExtension<T>(string className) where T : class
        {
            Type type = TypeCache.GetTypesDerivedFrom<T>()
                .FirstOrDefault(candidate => candidate.FullName == className);
            if (type == null)
                throw new BuildFailedException($"未找到 YooAsset 构建扩展：{className}");
            return Activator.CreateInstance(type) as T
                ?? throw new BuildFailedException($"无法创建 YooAsset 构建扩展：{className}");
        }

        public static string GetLastBuildVersion(PulletYooAssetSettings settings)
        {
            string packageName = PulletYooAssetSettingsEditor.GetSelectedPackageName(settings);
            string recorded = settings.GetLastBuildVersion(
                EditorUserBuildSettings.activeBuildTarget.ToString(), packageName);
            if (!string.IsNullOrWhiteSpace(recorded))
                return recorded;
            return string.Empty;
        }

        private static string CreateBuildVersion(PulletYooAssetSettings settings)
        {
            return settings.packageVersionMode == EPulletYooAssetPackageVersionMode.DateTime
                ? DateTime.Now.ToString("yyyy-MM-dd-HHmmss")
                : settings.packageVersion.Trim();
        }

        private static string PreviewBuildVersion(PulletYooAssetSettings settings)
        {
            return settings.packageVersionMode == EPulletYooAssetPackageVersionMode.DateTime
                ? DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + "（构建时生成）"
                : settings.packageVersion;
        }

        private static string GetVersionModeName(EPulletYooAssetPackageVersionMode mode)
        {
            return mode == EPulletYooAssetPackageVersionMode.DateTime ? "自动日期" : "手动版本";
        }
    }
}
