using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using PulletFramework.HybridCLR;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    public class HybridCLRCommand
    {
        public static string AOTAssembliesPath = "Art/Assembly/AOT";
        public static string HotUpdateAssembliesPath = "Art/Assembly/HotUpdate";
        public static string ManifestAssetPath = "Assets/Art/Assembly/PulletHotUpdateManifest.json";

        public static string[] GetHotUpdateAssemblyNames()
        {
            return SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.ToArray();
        }

        public static string[] GetAOTAssemblyNames()
        {
            return SettingsUtil.HybridCLRSettings.patchAOTAssemblies ?? System.Array.Empty<string>();
        }

        public static bool IsInstalled => new InstallerController().HasInstalledHybridCLR();

        /// <summary>验证安装与热更新程序集配置，不生成 DLL，也不触发 Player 构建。</summary>
        public static void ValidateIntegration()
        {
            if (!IsInstalled)
                throw new InvalidOperationException(
                    "HybridCLR 尚未初始化，请先打开 HybridCLR/Installer 完成安装。");
            if (GetHotUpdateAssemblyNames().Length == 0)
                throw new InvalidOperationException(
                    "尚未配置热更新程序集，请先打开 Project Settings/HybridCLR Settings。");
        }

        /// <summary>
        /// 生成AOT Dlls
        /// </summary>
        public static void GenerateAOTDlls()
        {
            StripAOTDllCommand.GenerateStripedAOTDlls(EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 生成AOT Dlls
        /// </summary>
        /// <param name="target"></param>
        public static void GenerateAOTDlls(BuildTarget target)
        {
            StripAOTDllCommand.GenerateStripedAOTDlls(target);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 编译热梗Dll
        /// </summary>
        public static void CompileDll()
        {
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 编译热梗Dll
        /// </summary>
        /// <param name="target"></param>
        public static void CompileDll(BuildTarget target)
        {
            CompileDllCommand.CompileDll(target);
            AssetDatabase.Refresh();
        }


        public static void BuildAndCopyDlls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            ValidateIntegration();
            PrebuildCommand.GenerateAll();
            CopyAOTHotUpdateDlls(target);
        }

        public static void CopyAOTHotUpdateDlls(BuildTarget target)
        {
            CopyAOTAssembliesToAssetPath(target);
            CopyHotUpdateAssembliesToAssetPath(target);
            GenerateManifest(null);
            AssetDatabase.Refresh();
        }

        /// <summary>根据已复制的 DLL 自动生成运行时清单。</summary>
        public static void GenerateManifest()
        {
            GenerateManifest(null);
        }

        public static void GenerateManifest(string contentVersion)
        {
            string[] aotNames = GetAOTAssemblyNames();
            string[] hotNames = GetHotUpdateAssemblyNames();
            PulletHybridCLRSettings entrySettings = PulletHybridCLRSettings.instance;
            ValidateEntrySettings(entrySettings, hotNames);
            var hotNameSet = new System.Collections.Generic.HashSet<string>(
                hotNames, StringComparer.OrdinalIgnoreCase);
            var entries = new System.Collections.Generic.List<PulletHotUpdateAssemblyInfo>();

            for (int i = 0; i < aotNames.Length; i++)
                entries.Add(CreateManifestEntry(aotNames[i], AOTAssembliesPath,
                    EPulletHotUpdateAssemblyKind.AotMetadata, hotNameSet));
            for (int i = 0; i < hotNames.Length; i++)
                entries.Add(CreateManifestEntry(hotNames[i], HotUpdateAssembliesPath,
                    EPulletHotUpdateAssemblyKind.HotUpdate, hotNameSet));

            var manifest = new PulletHotUpdateManifest
            {
                contentVersion = string.IsNullOrWhiteSpace(contentVersion)
                    ? CreateContentVersion(entries, entrySettings,
                        PlayerSettings.bundleVersion)
                    : contentVersion.Trim(),
                compatiblePlayerVersions = new[] { PlayerSettings.bundleVersion },
                entryAssembly = entrySettings.EntryAssembly,
                entryType = entrySettings.EntryType,
                entryMethod = entrySettings.EntryMethod,
                assemblies = entries.ToArray()
            };
            string absolutePath = Path.GetFullPath(ManifestAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, manifest.ToJson(true));
            AssetDatabase.ImportAsset(ManifestAssetPath, ImportAssetOptions.ForceUpdate);
            PLogger.EditorInfo($"HybridCLR 热更新清单已生成：{ManifestAssetPath}");
        }

        private static void ValidateEntrySettings(PulletHybridCLRSettings settings,
            string[] hotAssemblyNames)
        {
            if (string.IsNullOrWhiteSpace(settings.EntryAssembly)
                || string.IsNullOrWhiteSpace(settings.EntryType)
                || string.IsNullOrWhiteSpace(settings.EntryMethod))
            {
                throw new InvalidOperationException(
                    "尚未配置热更新入口，请在 Pullets/Workspace/HybridCLR 中填写入口信息。");
            }
            if (!hotAssemblyNames.Contains(
                    settings.EntryAssembly, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"入口程序集 {settings.EntryAssembly} 不在 HybridCLR 热更新程序集列表中。");
            }
        }

        /// <summary>
        /// copy AOTdll
        /// </summary>
        public static void CopyAOTAssembliesToAssetPath()
        {
            CopyAOTAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void CopyAOTAssembliesToAssetPath(BuildTarget target)
        {
            string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string aotAssembliesDstDir = Path.Combine(Application.dataPath, AOTAssembliesPath);
            string[] assemblyNames = GetAOTAssemblyNames();
            if (assemblyNames.Length == 0)
            {
                RemoveStaleAssemblies(aotAssembliesDstDir, Array.Empty<string>());
                PLogger.EditorWarning("HybridCLR 未配置 AOT 补充元数据程序集，跳过 AOT DLL 复制。");
                return;
            }

            CopyAssemblies(aotAssembliesSrcDir, aotAssembliesDstDir,
                assemblyNames, "AOT 补充元数据");
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// copy HotUpdate dll
        /// </summary>
        public static void CopyHotUpdateAssembliesToAssetPath()
        {
            CopyHotUpdateAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void CopyHotUpdateAssembliesToAssetPath(BuildTarget target)
        {
            string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotfixAssembliesDstDir = Path.Combine(Application.dataPath,
                HotUpdateAssembliesPath);
            string[] assemblyNames = GetHotUpdateAssemblyNames();
            if (assemblyNames.Length == 0)
                throw new InvalidOperationException("HybridCLR 未配置热更新程序集，无法复制 DLL。");

            CopyAssemblies(hotfixDllSrcDir, hotfixAssembliesDstDir,
                assemblyNames, "热更新");
            AssetDatabase.Refresh();
        }

        private static void CopyAssemblies(string sourceDirectory, string destinationDirectory,
            string[] assemblyNames, string category)
        {
            string[] sourceFiles = assemblyNames
                .Select(name => Path.Combine(sourceDirectory, name + ".dll"))
                .ToArray();
            string[] missingFiles = sourceFiles.Where(path => !File.Exists(path)).ToArray();
            if (missingFiles.Length > 0)
            {
                throw new FileNotFoundException(
                    $"{category} DLL 尚未完整生成，未改动现有输出目录。缺少：\n" +
                    string.Join("\n", missingFiles));
            }

            Directory.CreateDirectory(destinationDirectory);
            RemoveStaleAssemblies(destinationDirectory, assemblyNames);
            for (int i = 0; i < assemblyNames.Length; i++)
            {
                string destination = Path.Combine(destinationDirectory,
                    assemblyNames[i] + ".bytes");
                File.Copy(sourceFiles[i], destination, true);
            }

            PLogger.EditorInfo(
                $"HybridCLR 已复制 {assemblyNames.Length} 个{category} DLL 到：{destinationDirectory}");
        }

        private static PulletHotUpdateAssemblyInfo CreateManifestEntry(string assemblyName,
            string relativeDirectory, EPulletHotUpdateAssemblyKind kind,
            System.Collections.Generic.HashSet<string> hotNames)
        {
            string relativePath = $"Assets/{relativeDirectory}/{assemblyName}.bytes"
                .Replace('\\', '/');
            string absolutePath = Path.GetFullPath(relativePath);
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"生成清单时找不到程序集：{relativePath}", absolutePath);
            byte[] bytes = File.ReadAllBytes(absolutePath);
            string[] dependencies = kind == EPulletHotUpdateAssemblyKind.HotUpdate
                ? ReadHotUpdateDependencies(bytes, hotNames)
                : Array.Empty<string>();
            return new PulletHotUpdateAssemblyInfo
            {
                name = assemblyName,
                version = ReadAssemblyVersion(bytes),
                kind = kind,
                location = assemblyName,
                byteLength = bytes.LongLength,
                sha256 = ComputeSha256(bytes),
                dependencies = dependencies
            };
        }

        private static string[] ReadHotUpdateDependencies(byte[] bytes,
            System.Collections.Generic.HashSet<string> hotNames)
        {
            try
            {
                Assembly assembly = Assembly.ReflectionOnlyLoad(bytes);
                return assembly.GetReferencedAssemblies()
                    .Select(reference => reference.Name)
                    .Where(hotNames.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "无法读取热更新 DLL 的程序集依赖，清单未生成。", exception);
            }
        }

        private static string ReadAssemblyVersion(byte[] bytes)
        {
            try
            {
                return Assembly.ReflectionOnlyLoad(bytes).GetName().Version?.ToString()
                    ?? "0.0.0.0";
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("无法读取 DLL 程序集版本。", exception);
            }
        }

        private static string CreateContentVersion(
            System.Collections.Generic.List<PulletHotUpdateAssemblyInfo> entries,
            PulletHybridCLRSettings entrySettings, string playerVersion)
        {
            string assemblyFingerprint = string.Join("|", entries
                .OrderBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.name + ":" + entry.sha256));
            string fingerprint = string.Join("|",
                playerVersion,
                entrySettings.EntryAssembly,
                entrySettings.EntryType,
                entrySettings.EntryMethod,
                assemblyFingerprint);
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fingerprint));
                return "sha256-" + BitConverter.ToString(hash, 0, 8)
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void RemoveStaleAssemblies(string directory, string[] assemblyNames)
        {
            if (!Directory.Exists(directory))
                return;
            var keep = new System.Collections.Generic.HashSet<string>(
                assemblyNames.Select(name => name + ".bytes"),
                StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(directory, "*.bytes"))
            {
                if (keep.Contains(Path.GetFileName(file)))
                    continue;
                string assetPath = "Assets/" + file.Substring(Application.dataPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                if (!AssetDatabase.DeleteAsset(assetPath))
                    throw new IOException($"无法删除过期程序集资源：{assetPath}");
            }
        }
    }
}
