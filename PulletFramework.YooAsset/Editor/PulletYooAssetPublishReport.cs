using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>生成可供 CDN 上传器消费的 YooAsset 发布清单。</summary>
    public static class PulletYooAssetPublishReport
    {
        [Serializable]
        public sealed class PublishReport
        {
            public int schemaVersion = 1;
            public string generatedAtUtc;
            public string packageName;
            public string packageVersion;
            public string sourceDirectory;
            public long totalBytes;
            public List<PublishFile> files = new List<PublishFile>();
        }

        [Serializable]
        public sealed class PublishFile
        {
            public int uploadPhase;
            public string role;
            public string relativePath;
            public long bytes;
            public string sha256;
        }

        /// <summary>
        /// 扫描构建版本目录并生成发布报告。阶段 1 是资源文件，阶段 2 是清单，阶段 3 是版本指针。
        /// </summary>
        public static string Create(string packageDirectory, string packageName, string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
                throw new DirectoryNotFoundException($"YooAsset package directory not found: {packageDirectory}");
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Package name is required.", nameof(packageName));
            if (string.IsNullOrWhiteSpace(packageVersion))
                throw new ArgumentException("Package version is required.", nameof(packageVersion));

            string versionFileName = FindSingleFileName(
                packageDirectory, $"{packageName}.version", $"_{packageName}.version");
            string manifestFileName = FindSingleFileName(
                packageDirectory, $"{packageName}_{packageVersion}.bytes",
                $"_{packageName}_{packageVersion}.bytes");
            string manifestHashFileName = Path.GetFileNameWithoutExtension(manifestFileName) + ".hash";
            RequireFile(Path.Combine(packageDirectory, manifestHashFileName));

            var report = new PublishReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                packageName = packageName,
                packageVersion = packageVersion,
                sourceDirectory = Path.GetFullPath(packageDirectory).Replace('\\', '/')
            };

            foreach (string path in Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(path);
                if (IsBuildOnlyFile(fileName))
                    continue;

                int phase = fileName == versionFileName ? 3 :
                    fileName == manifestFileName || fileName == manifestHashFileName ? 2 : 1;
                var info = new FileInfo(path);
                report.files.Add(new PublishFile
                {
                    uploadPhase = phase,
                    role = phase == 1 ? "Payload" : phase == 2 ? "Manifest" : "VersionPointer",
                    relativePath = MakeRelativePath(packageDirectory, path),
                    bytes = info.Length,
                    sha256 = ComputeSha256(path)
                });
                report.totalBytes += info.Length;
            }

            report.files = report.files
                .OrderBy(item => item.uploadPhase)
                .ThenBy(item => item.relativePath, StringComparer.Ordinal)
                .ToList();

            string reportDirectory = Path.Combine(Directory.GetParent(packageDirectory).FullName, "PublishReports");
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory,
                $"{packageName}_{packageVersion}_publish.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            return reportPath.Replace('\\', '/');
        }

        /// <summary>读取并校验已有发布报告。</summary>
        public static PublishReport Load(string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
                throw new FileNotFoundException("YooAsset publish report not found.", reportPath);
            PublishReport report = JsonUtility.FromJson<PublishReport>(File.ReadAllText(reportPath));
            if (report == null || report.schemaVersion != 1 || report.files == null)
                throw new InvalidDataException($"Unsupported YooAsset publish report: {reportPath}");
            return report;
        }

        public static void Validate(
            PublishReport report, string expectedDirectory,
            string expectedPackage, string expectedVersion)
        {
            if (report == null || report.files == null
                || !string.Equals(report.packageName, expectedPackage, StringComparison.Ordinal)
                || !string.Equals(report.packageVersion, expectedVersion, StringComparison.Ordinal)
                || !string.Equals(Path.GetFullPath(report.sourceDirectory),
                    Path.GetFullPath(expectedDirectory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("发布报告与当前构建包、版本或目录不一致，请重新生成报告。");

            string root = Path.GetFullPath(expectedDirectory).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int pointerCount = 0;
            int manifestCount = 0;
            for (int index = 0; index < report.files.Count; index++)
            {
                PublishFile file = report.files[index];
                if (file == null || string.IsNullOrWhiteSpace(file.relativePath)
                    || Path.IsPathRooted(file.relativePath)
                    || !seen.Add(file.relativePath))
                    throw new InvalidDataException("发布报告包含无效或重复的文件路径。");
                string fullPath = Path.GetFullPath(Path.Combine(
                    expectedDirectory, file.relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"发布报告文件越过构建目录：{file.relativePath}");
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("发布报告中的构建文件不存在。", fullPath);
                var info = new FileInfo(fullPath);
                if (info.Length != file.bytes
                    || !string.Equals(ComputeSha256(fullPath), file.sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"发布报告文件内容已变化，请重新构建并生成报告：{file.relativePath}");
                if (file.uploadPhase == 3 && file.role == "VersionPointer"
                    && fullPath.EndsWith(".version", StringComparison.OrdinalIgnoreCase))
                    pointerCount++;
                else if (file.uploadPhase == 2 && file.role == "Manifest")
                    manifestCount++;
                else if (file.uploadPhase != 1 || file.role != "Payload")
                    throw new InvalidDataException($"发布报告文件阶段无效：{file.relativePath}");
            }
            if (pointerCount != 1 || manifestCount < 2)
                throw new InvalidDataException("发布报告缺少唯一版本指针或必要的清单文件。");
        }

        private static bool IsBuildOnlyFile(string fileName)
        {
            return fileName.Equals("buildlogtep.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("link.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".report", StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
                throw new InvalidDataException($"Required YooAsset publish file is missing: {path}");
        }

        private static string FindSingleFileName(
            string directory, string exactFileName, string suffix)
        {
            string[] matches = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name.Equals(exactFileName, StringComparison.Ordinal)
                    || name.EndsWith(suffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(
                    $"Expected one YooAsset publish file matching '{exactFileName}', found {matches.Length}.");
            return matches[0];
        }

        private static string MakeRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
