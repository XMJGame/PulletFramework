using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PulletFramework.AssetPublishing.Editor;
using PulletFramework;
using PulletFramework.MiniGame.Platform;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    /// <summary>规划并上传 Unity WebGL data 首包；与 YooAsset 业务资源发布相互独立。</summary>
    public static class MiniGameFirstPackageCdnPublisher
    {
        private const string ImmutableCacheControl = "public, max-age=31536000, immutable";
        private static bool s_isUploading;
        private static string s_cachedPackagePath;
        private static long s_cachedPackageSize;
        private static double s_nextPackageSizeRefresh;

        public static bool IsUploading => s_isUploading;

        public static bool TryGetExistingPackageSize(
            string platformId,
            MiniGamePlatformSettings settings,
            out long size,
            out string packagePath)
        {
            if (string.IsNullOrWhiteSpace(settings?.outputPath))
            {
                size = 0;
                packagePath = null;
                return false;
            }

            string folder = platformId == PulletPlatformIds.Douyin
                ? "tt-minigame"
                : platformId == PulletPlatformIds.WeChat ? "minigame" : null;
            packagePath = folder == null
                ? null
                : Path.Combine(ResolveOutputPath(settings.outputPath), folder);
            if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(packagePath))
            {
                size = 0;
                return false;
            }

            if (!string.Equals(packagePath, s_cachedPackagePath, StringComparison.OrdinalIgnoreCase)
                || EditorApplication.timeSinceStartup >= s_nextPackageSizeRefresh)
            {
                s_cachedPackagePath = packagePath;
                s_cachedPackageSize = new DirectoryInfo(packagePath)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
                s_nextPackageSizeRefresh = EditorApplication.timeSinceStartup + 3d;
            }

            size = s_cachedPackageSize;
            return true;
        }

        public static void PrepareBuildSettings(
            string platformId, string playerVersion, MiniGamePlatformSettings settings)
        {
            if (settings.firstPackageResourceMode != EFirstPackageResourceMode.Cdn)
                return;

            if (settings.manuallyConfigureFirstPackageCdn)
            {
                if (string.IsNullOrWhiteSpace(settings.cdnUrl))
                    throw new InvalidOperationException("手动首包 CDN 地址不能为空。");
                return;
            }

            if (!TryComposeCdnUrl(platformId, playerVersion, settings,
                    out string url, out string error))
                throw new InvalidOperationException($"无法生成首包 CDN 地址：{error}");

            settings.cdnUrl = url;
            EditorUtility.SetDirty(settings);
        }

        /// <summary>校验转换 SDK 的首包产物，并修正部分 SDK 版本生成的错误加载标记。</summary>
        public static void ReconcileExport(
            string platformId, MiniGamePlatformSettings settings)
        {
            string outputRoot = ResolveOutputPath(settings.outputPath);
            string miniGameFolder = GetMiniGameFolder(platformId);
            string gameScriptPath = Path.Combine(outputRoot, miniGameFolder, "game.js");
            if (!File.Exists(gameScriptPath))
                throw new FileNotFoundException("小游戏导出结果缺少 game.js。", gameScriptPath);

            string gameScript = File.ReadAllText(gameScriptPath);
            var loadModePattern = new Regex(
                @"(loadDataPackageFromSubpackage\s*:\s*)(true|false)",
                RegexOptions.IgnoreCase);
            if (!loadModePattern.IsMatch(gameScript))
                throw new InvalidDataException(
                    "game.js 中没有 loadDataPackageFromSubpackage，无法确认首包加载方式。");

            bool useCdn = settings.firstPackageResourceMode == EFirstPackageResourceMode.Cdn;
            if (useCdn)
                FindDataFile(outputRoot, gameScript);

            string desiredValue = useCdn ? "false" : "true";
            string patched = loadModePattern.Replace(
                gameScript, match => match.Groups[1].Value + desiredValue, 1);
            if (!string.Equals(gameScript, patched, StringComparison.Ordinal))
            {
                File.WriteAllText(gameScriptPath, patched, new UTF8Encoding(false));
                PLogger.EditorWarning(
                    $"[PulletFramework.MiniGame] 已修正 {platformId} 导出包首包加载标记为 {desiredValue}。"
                    + "转换 SDK 生成的标记与当前配置不一致。");
            }
        }

        public static bool TryComposeCdnUrl(
            string platformId,
            string playerVersion,
            MiniGamePlatformSettings settings,
            out string url,
            out string error)
        {
            url = null;
            if (!TryBuildRelativeDirectory(platformId, playerVersion,
                    settings.firstPackageCdnFolder, out string directory, out error))
                return false;

            if (!PulletAssetPublishingService.TryBuildPublicUrl(directory, out url, out error))
                return false;

            url = EnsureTrailingSlash(url);
            return true;
        }

        public static async void PublishFromWindow(
            string platformId,
            string playerVersion,
            MiniGamePlatformSettings settings)
        {
            if (s_isUploading)
                return;

            try
            {
                s_isUploading = true;
                if (settings.manuallyConfigureFirstPackageCdn)
                    throw new InvalidOperationException(
                        "自动上传只支持由“资源发布”配置生成的 CDN 地址。手动地址请使用对应供应商工具上传。");
                PrepareBuildSettings(platformId, playerVersion, settings);
                MiniGameFirstPackageArtifact artifact = FindArtifact(
                    platformId, playerVersion, settings);
                PulletAssetPublishingSession session =
                    PulletAssetPublishingService.CreateSession();
                string plannedUrl = session.BuildPublicUrl(artifact.ObjectKey);
                if (!UrlsEqual(plannedUrl, artifact.PublicUrl))
                    throw new InvalidOperationException(
                        "首包供应商配置在规划后发生变化，请重新检查 CDN 地址后重试。");

                if (!EditorUtility.DisplayDialog(
                        "上传首包 Data CDN",
                        $"平台：{platformId}\n文件：{artifact.FileName}\n"
                        + $"大小：{FormatBytes(artifact.Size)}\n目标：{artifact.PublicUrl}\n\n"
                        + "将配置小游戏下载 CORS，并上传该文件。是否继续？",
                        "上传", "取消"))
                    return;

                string uploadedUrl;
                using (session.AcquirePublication(artifact.ObjectKey))
                {
                    session.EnsureMiniGameDownloadCors();
                    EditorUtility.DisplayProgressBar(
                        "上传首包 Data CDN", artifact.FileName, 0.05f);
                    uploadedUrl = await session.UploadAsync(
                        artifact.ObjectKey,
                        artifact.SourcePath,
                        null,
                        ImmutableCacheControl,
                        "application/octet-stream");
                }

                if (!UrlsEqual(uploadedUrl, artifact.PublicUrl))
                    throw new InvalidOperationException(
                        $"上传地址与构建配置不一致。\n构建请求：{artifact.PublicUrl}\n实际上传：{uploadedUrl}");

                PLogger.EditorInfo(
                    $"[PulletFramework.MiniGame] First package CDN upload completed: {uploadedUrl}");
                EditorUtility.DisplayDialog(
                    "首包上传完成",
                    $"已上传：\n{uploadedUrl}\n\n"
                    + "下一步请在平台后台确认该域名已加入下载合法域名，然后重新编译或预览小游戏。",
                    "确定");
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, "[PulletFramework.MiniGame] 首包 Data CDN 上传失败。");
                EditorUtility.DisplayDialog("首包上传失败", exception.Message, "确定");
            }
            finally
            {
                s_isUploading = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static MiniGameFirstPackageArtifact FindArtifact(
            string platformId,
            string playerVersion,
            MiniGamePlatformSettings settings)
        {
            if (settings.firstPackageResourceMode != EFirstPackageResourceMode.Cdn)
                throw new InvalidOperationException("首包资源尚未选择 CDN 模式。");

            string outputRoot = ResolveOutputPath(settings.outputPath);
            string miniGameFolder = GetMiniGameFolder(platformId);

            string gameScriptPath = Path.Combine(outputRoot, miniGameFolder, "game.js");
            if (!File.Exists(gameScriptPath))
                throw new FileNotFoundException(
                    "没有找到小游戏导出结果，请先使用 CDN 模式重新构建当前平台。", gameScriptPath);

            string gameScript = File.ReadAllText(gameScriptPath);
            ReconcileExport(platformId, settings);
            gameScript = File.ReadAllText(gameScriptPath);
            Match packageMode = Regex.Match(gameScript,
                @"loadDataPackageFromSubpackage\s*:\s*(true|false)",
                RegexOptions.IgnoreCase);
            if (!packageMode.Success
                || !string.Equals(packageMode.Groups[1].Value, "false", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "现有导出包仍使用 Package 分包。请选择 CDN 后重新构建，再执行上传。");

            FileInfo source = FindDataFile(outputRoot, gameScript);

            if (!TryBuildRelativeDirectory(platformId, playerVersion,
                    settings.firstPackageCdnFolder, out string directory, out string error))
                throw new InvalidOperationException(error);

            string objectKey = CombineKey(directory, source.Name);
            if (!PulletAssetPublishingService.TryBuildPublicUrl(
                    objectKey, out string publicUrl, out error))
                throw new InvalidOperationException(error);

            string expectedUrl = EnsureTrailingSlash(settings.cdnUrl) + source.Name;
            if (!UrlsEqual(expectedUrl, publicUrl))
                throw new InvalidOperationException(
                    $"首包 CDN 配置与上传目录不一致。\n构建请求：{expectedUrl}\n计划上传：{publicUrl}\n"
                    + "请重新构建后再上传。");

            return new MiniGameFirstPackageArtifact(
                source.FullName, source.Name, source.Length, objectKey, publicUrl);
        }

        private static bool TryBuildRelativeDirectory(
            string platformId,
            string playerVersion,
            string configuredFolder,
            out string directory,
            out string error)
        {
            directory = null;
            string folder = string.IsNullOrWhiteSpace(configuredFolder)
                ? "bootstrap"
                : configuredFolder.Replace('\\', '/').Trim('/');
            if (folder.Contains("..") || folder.Contains("://"))
            {
                error = "首包 CDN 子目录必须是对象存储中的相对目录，不能包含 '..' 或完整 URL。";
                return false;
            }

            string version = NormalizeSegment(playerVersion);
            if (string.IsNullOrEmpty(version))
            {
                error = "Player 版本不能为空。";
                return false;
            }

            directory = CombineKey(folder, NormalizeSegment(platformId), version);
            error = null;
            return true;
        }

        private static string ResolveOutputPath(string configuredPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(projectRoot, configuredPath));
        }

        private static string GetMiniGameFolder(string platformId)
        {
            if (platformId == PulletPlatformIds.Douyin)
                return "tt-minigame";
            if (platformId == PulletPlatformIds.WeChat)
                return "minigame";
            throw new NotSupportedException($"尚未支持平台首包处理：{platformId}");
        }

        private static FileInfo FindDataFile(string outputRoot, string gameScript)
        {
            Match md5Match = Regex.Match(gameScript,
                "DATA_FILE_MD5\\s*:\\s*['\\\"](?<md5>[^'\\\"]+)['\\\"]");
            if (!md5Match.Success)
                throw new InvalidDataException("game.js 中没有 DATA_FILE_MD5，无法定位首包数据文件。");

            string webglFolder = Path.Combine(outputRoot, "webgl");
            string md5 = md5Match.Groups["md5"].Value;
            FileInfo source = Directory.Exists(webglFolder)
                ? new DirectoryInfo(webglFolder).EnumerateFiles(
                        md5 + ".webgl.data.unityweb.bin*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(file => file.Extension.Equals(".br", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault()
                : null;
            if (source == null)
                throw new FileNotFoundException(
                    $"没有找到 CDN 首包文件。期望位于 {webglFolder}，并以 {md5}.webgl.data 开头。"
                    + "请确认已启用首包压缩并重新构建。");
            return source;
        }

        private static string CombineKey(params string[] parts)
        {
            return string.Join("/", parts.Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Replace('\\', '/').Trim('/')));
        }

        private static string NormalizeSegment(string value)
        {
            return Regex.Replace(value?.Trim() ?? string.Empty, @"[^A-Za-z0-9._-]", "-");
        }

        private static string EnsureTrailingSlash(string value)
        {
            return (value ?? string.Empty).TrimEnd('/') + "/";
        }

        private static bool UrlsEqual(string left, string right)
        {
            return string.Equals(left?.TrimEnd('/'), right?.TrimEnd('/'),
                StringComparison.Ordinal);
        }

        public static string FormatBytes(long bytes)
        {
            const double megabyte = 1024d * 1024d;
            return bytes >= megabyte
                ? $"{bytes / megabyte:F2} MB"
                : $"{bytes / 1024d:F1} KB";
        }

        private sealed class MiniGameFirstPackageArtifact
        {
            public string SourcePath { get; }
            public string FileName { get; }
            public long Size { get; }
            public string ObjectKey { get; }
            public string PublicUrl { get; }

            public MiniGameFirstPackageArtifact(
                string sourcePath, string fileName, long size, string objectKey, string publicUrl)
            {
                SourcePath = sourcePath;
                FileName = fileName;
                Size = size;
                ObjectKey = objectKey;
                PublicUrl = publicUrl;
            }
        }
    }
}
