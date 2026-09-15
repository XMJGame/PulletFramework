using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using YooAsset.Editor;

namespace PulletFramework.Editor
{
    public readonly struct PulletShaderVariantCollectionResult
    {
        public string AssetPath { get; }
        public int MaterialCount { get; }
        public int ShaderCount { get; }
        public int VariantCount { get; }
        public int SkippedVariantCount { get; }

        public PulletShaderVariantCollectionResult(
            string assetPath, int materialCount, int shaderCount,
            int variantCount, int skippedVariantCount)
        {
            AssetPath = assetPath;
            MaterialCount = materialCount;
            ShaderCount = shaderCount;
            VariantCount = variantCount;
            SkippedVariantCount = skippedVariantCount;
        }
    }

    /// <summary>使用公开编辑器 API 收集指定 YooAsset Package 的材质变体。</summary>
    public static class PulletYooAssetShaderVariantCollector
    {
        private const string DefaultGeneratedRoot = "Assets/Generated/Pullet/ShaderVariants";
        private const string LegacyGeneratedRoot = "Assets/Generated/Pullet/YooAsset/ShaderVariants";
        private const string CollectorGroupName = "PulletGeneratedShaderVariants";
        private static readonly ShaderTagId LightModeTag = new ShaderTagId("LightMode");
        private static readonly ShaderTagId RenderPipelineTag = new ShaderTagId("RenderPipeline");

        public static string GetAssetPath(PulletYooAssetSettings settings, string packageName)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            string directory = NormalizeOutputDirectory(settings.shaderVariantOutputDirectory);
            string location = PulletYooAssetShaderVariants.GetLocation(settings, packageName);
            return $"{directory}/{location}.shadervariants";
        }

        public static void CollectSelectedPackageBatch()
        {
            PulletYooAssetSettings settings = PulletYooAssetSettingsEditor.LoadOrCreate();
            Collect(settings, PulletYooAssetSettingsEditor.GetSelectedPackageName(settings));
        }

        public static PulletShaderVariantCollectionResult Collect(string packageName)
        {
            return Collect(PulletYooAssetSettingsEditor.LoadOrCreate(), packageName);
        }

        public static PulletShaderVariantCollectionResult Collect(
            PulletYooAssetSettings settings, string packageName)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentNullException(nameof(packageName));

            CollectResult collectResult = BundleCollectorSettingData.Setting.BeginCollect(
                packageName, false, false);
            List<string> materialPaths = GetMaterialPaths(collectResult);
            var collection = new ShaderVariantCollection();
            var shaders = new HashSet<Shader>();
            int skippedCount = 0;

            try
            {
                for (int index = 0; index < materialPaths.Count; index++)
                {
                    string materialPath = materialPaths[index];
                    EditorUtility.DisplayProgressBar(
                        "收集着色器变体", materialPath,
                        materialPaths.Count == 0 ? 1f : (float)index / materialPaths.Count);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null || material.shader == null)
                        continue;
                    shaders.Add(material.shader);
                    skippedCount += AddMaterialVariants(collection, material);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string assetPath = GetAssetPath(settings, packageName);
            EnsureAssetFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(collection, assetPath);
            AssetDatabase.SaveAssets();
            EnsureYooAssetCollector(packageName, assetPath);

            var result = new PulletShaderVariantCollectionResult(
                assetPath, materialPaths.Count, shaders.Count,
                collection.variantCount, skippedCount);
            PLogger.EditorInfo(
                $"[PulletYooAsset] 着色器变体收集完成：{packageName}\n" +
                $"材质 {result.MaterialCount}，Shader {result.ShaderCount}，" +
                $"变体 {result.VariantCount}，跳过 {result.SkippedVariantCount}\n" +
                result.AssetPath);
            return result;
        }

        private static List<string> GetMaterialPaths(CollectResult collectResult)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CollectAssetInfo asset in collectResult.CollectAssets)
            {
                if (asset.AssetInfo.AssetType == typeof(Material))
                    paths.Add(asset.AssetInfo.AssetPath);
                foreach (EditorAssetInfo dependency in asset.DependAssets)
                {
                    if (dependency.AssetType == typeof(Material))
                        paths.Add(dependency.AssetPath);
                }
            }
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static int AddMaterialVariants(
            ShaderVariantCollection collection, Material material)
        {
            Shader shader = material.shader;
            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subshader = shaderData.ActiveSubshader;
            bool isScriptablePipeline = string.Equals(
                subshader.FindTagValue(RenderPipelineTag).name,
                "UniversalPipeline", StringComparison.OrdinalIgnoreCase);
            string[] keywords = material.shaderKeywords
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(keyword => keyword, StringComparer.Ordinal)
                .ToArray();
            int skippedCount = 0;

            for (int passIndex = 0; passIndex < subshader.PassCount; passIndex++)
            {
                ShaderData.Pass pass = subshader.GetPass(passIndex);
                if (!string.IsNullOrEmpty(pass.Name)
                    && !material.GetShaderPassEnabled(pass.Name))
                    continue;

                PassType passType = ResolvePassType(
                    pass.FindTagValue(LightModeTag).name, isScriptablePipeline);
                try
                {
                    collection.Add(new ShaderVariantCollection.ShaderVariant(
                        shader, passType, keywords));
                }
                catch (ArgumentException)
                {
                    skippedCount++;
                }
            }
            return skippedCount;
        }

        private static PassType ResolvePassType(string lightMode, bool isScriptablePipeline)
        {
            switch (lightMode)
            {
                case "ForwardBase": return PassType.ForwardBase;
                case "ForwardAdd": return PassType.ForwardAdd;
                case "Deferred": return PassType.Deferred;
                case "ShadowCaster": return PassType.ShadowCaster;
                case "Meta": return PassType.Meta;
                case "MotionVectors": return PassType.MotionVectors;
                case "SRPDefaultUnlit": return PassType.ScriptableRenderPipelineDefaultUnlit;
                default:
                    return isScriptablePipeline
                        ? string.IsNullOrEmpty(lightMode)
                            ? PassType.ScriptableRenderPipelineDefaultUnlit
                            : PassType.ScriptableRenderPipeline
                        : PassType.Normal;
            }
        }

        private static void EnsureYooAssetCollector(string packageName, string assetPath)
        {
            BundleCollectorPackage package = BundleCollectorSettingData.Setting.Packages
                .FirstOrDefault(item => string.Equals(
                    item.PackageName, packageName, StringComparison.Ordinal));
            if (package == null)
                throw new InvalidOperationException($"YooAsset Package 不存在：{packageName}");

            BundleCollectorGroup group = package.Groups.FirstOrDefault(item =>
                string.Equals(item.GroupName, CollectorGroupName, StringComparison.Ordinal));
            if (group == null)
                group = BundleCollectorSettingData.CreateGroup(package, CollectorGroupName);

            string legacyPath = $"{LegacyGeneratedRoot}/PulletShaderVariants_{packageName}.shadervariants";
            BundleCollector collector = group.Collectors.FirstOrDefault(item =>
                string.Equals(item.CollectPath, assetPath, StringComparison.OrdinalIgnoreCase))
                ?? group.Collectors.FirstOrDefault(item =>
                    string.Equals(item.UserData, packageName, StringComparison.Ordinal))
                ?? group.Collectors.FirstOrDefault(item =>
                    string.Equals(item.CollectPath, legacyPath, StringComparison.OrdinalIgnoreCase));
            if (collector == null)
            {
                collector = new BundleCollector();
                BundleCollectorSettingData.CreateCollector(group, collector);
            }
            string previousPath = collector.CollectPath;
            collector.CollectPath = assetPath;
            collector.CollectorGUID = AssetDatabase.AssetPathToGUID(assetPath);
            collector.CollectorType = ECollectorType.MainAssetCollector;
            collector.AddressRuleName = nameof(AddressByFileName);
            collector.PackRuleName = nameof(PackShaderVariants);
            collector.FilterRuleName = nameof(CollectAll);
            collector.UserData = packageName;
            BundleCollectorSettingData.ModifyCollector(group, collector);
            BundleCollectorSettingData.SaveFile();
            if (!string.IsNullOrWhiteSpace(previousPath)
                && !string.Equals(previousPath, assetPath, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.DeleteAsset(previousPath);
        }

        private static string NormalizeOutputDirectory(string configuredPath)
        {
            string directory = string.IsNullOrWhiteSpace(configuredPath)
                ? DefaultGeneratedRoot
                : configuredPath.Trim().Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(directory, "Assets", StringComparison.Ordinal)
                && !directory.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("着色器变体输出目录必须位于项目 Assets 目录下。");
            if (directory.Split('/').Any(segment => segment == "." || segment == ".."))
                throw new InvalidOperationException("着色器变体输出目录不能包含相对路径片段。");
            return directory;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
