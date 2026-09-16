using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    /// <summary>处理小游戏 SDK 生成的加载页，不修改第三方插件模板。</summary>
    public static class MiniGameLoadingPagePostprocessor
    {
        private static readonly Regex IconVisibility = new Regex(
            @"(iconConfig\s*:\s*\{\s*visible\s*:\s*)true\b",
            RegexOptions.CultureInvariant);

        public static void Apply(string outputPath, bool showDefaultUnityLogo)
        {
            if (showDefaultUnityLogo)
                return;

            string path = Path.Combine(outputPath, "game.js");
            if (!File.Exists(path))
                throw new FileNotFoundException("小游戏导出结果中没有 game.js。", path);

            string content = File.ReadAllText(path, Encoding.UTF8);
            Match match = IconVisibility.Match(content);
            if (!match.Success)
                throw new InvalidOperationException(
                    "小游戏 SDK 的加载页模板已经变化，找不到 iconConfig.visible: true。");

            string updated = IconVisibility.Replace(content, "$1false", 1);
            File.WriteAllText(path, updated, new UTF8Encoding(false));
        }

        public static void ApplyDouyin(string outputPath, DouyinPlatformSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (settings.startupImage == null)
            {
                Apply(outputPath, settings.showDefaultUnityLoadingLogo);
                return;
            }

            string path = Path.Combine(outputPath, "game.js");
            if (!File.Exists(path))
                throw new FileNotFoundException("抖音导出结果中没有 game.js。", path);

            string content = File.ReadAllText(path, Encoding.UTF8);
            content = ReplaceSingle(content, @"(\bdisableLoadingPage\s*:\s*)(?:true|false)\b", "false");
            string imagePath = CopyImage(settings.startupImage, outputPath, "pullet_loading_page");
            content = ReplaceSingle(content, @"(\bbackgroundImage\s*:\s*)'[^']*'", $"'{imagePath}'");

            content = ReplaceSingle(content, @"(\biconConfig\s*:\s*\{\s*visible\s*:\s*)(?:true|false)\b",
                settings.showDefaultUnityLoadingLogo ? "true" : "false");
            content = ReplaceSingle(content, @"(\biconImage\s*:\s*)'[^']*'", "'images/unity_logo.png'");
            content = ReplaceSingle(content, @"(\bdesignWidth\s*:\s*)\d+", "375");
            content = ReplaceSingle(content, @"(\bdesignHeight\s*:\s*)\d+", "812");
            content = ReplaceSingle(content, @"(\bscaleMode\s*:\s*)[^,\r\n]+",
                "info.screenWidth / info.screenHeight > 0.5 ? scaleMode.showAll : scaleMode.noBorder");
            content = ReplaceStyle(content, "textConfig", "bottom", "194");
            content = ReplaceStyle(content, "textConfig", "height", "20");
            content = ReplaceStyle(content, "textConfig", "width", "320");
            content = ReplaceStyle(content, "textConfig", "fontSize", "14");
            content = ReplaceStyle(content, "barConfig", "width",
                settings.showEngineLoadingBar ? "280" : "0");
            content = ReplaceStyle(content, "barConfig", "height",
                settings.showEngineLoadingBar ? "12" : "0");
            content = ReplaceStyle(content, "barConfig", "bottom", "169");
            content = ReplaceStyle(content, "barConfig", "backgroundColor",
                $"'#{ColorUtility.ToHtmlStringRGB(settings.loadingBarBackgroundColor)}'");
            content = ReplaceStyle(content, "iconConfig", "width", "106");
            content = ReplaceStyle(content, "iconConfig", "height", "40");
            content = ReplaceStyle(content, "iconConfig", "bottom", "141");

            File.WriteAllText(path, content, new UTF8Encoding(false));
            DeleteLegacyImage(outputPath, "pullet_loading_background.jpg");
            DeleteLegacyImage(outputPath, "pullet_loading_overlay.png");
        }

        public static void RefreshExistingDouyinExport()
        {
            DouyinPlatformSettings settings = MiniGameBuildSettingsData.Douyin;
            if (string.IsNullOrWhiteSpace(settings.outputPath))
                throw new InvalidOperationException("请先配置抖音小游戏输出目录。");
            if (settings.startupImage == null)
                throw new InvalidOperationException(
                    "刷新现有导出包需要配置启动图；移除素材后请重新导出，以恢复 TTSDK 默认加载页配置。");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string exportRoot = Path.IsPathRooted(settings.outputPath)
                ? Path.GetFullPath(settings.outputPath)
                : Path.GetFullPath(Path.Combine(projectRoot, settings.outputPath));
            string artifactPath = Path.Combine(exportRoot, "tt-minigame");
            if (!File.Exists(Path.Combine(artifactPath, "game.js")))
                artifactPath = exportRoot;
            if (!File.Exists(Path.Combine(artifactPath, "game.js")))
                throw new FileNotFoundException("现有抖音导出包中没有 game.js，请先导出一次小游戏。", artifactPath);

            ApplyDouyin(artifactPath, settings);
            PulletFramework.PLogger.EditorInfo($"[PulletFramework.MiniGame] 已刷新抖音加载页：{artifactPath}");
        }

        private static string CopyImage(Texture2D image, string outputPath, string name)
        {
            string source = AssetDatabase.GetAssetPath(image);
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (!File.Exists(source) || (extension != ".png" && extension != ".jpg" && extension != ".jpeg"))
                throw new InvalidOperationException(
                    $"加载页素材 {image.name} 必须是 Assets 下的 PNG 或 JPEG 文件。当前路径：{source}");

            string imagesPath = Path.Combine(outputPath, "images");
            Directory.CreateDirectory(imagesPath);
            string fileName = name + extension;
            File.Copy(source, Path.Combine(imagesPath, fileName), true);
            return "images/" + fileName;
        }

        private static void DeleteLegacyImage(string outputPath, string fileName)
        {
            string path = Path.Combine(outputPath, "images", fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string ReplaceStyle(string content, string configName, string property, string value)
        {
            string pattern = @"\b" + Regex.Escape(configName)
                + @"\s*:\s*\{[\s\S]*?\bstyle\s*:\s*\{(?<body>[^}]*)\}";
            Match block = Regex.Match(content, pattern, RegexOptions.CultureInvariant);
            if (!block.Success || block.NextMatch().Success)
                throw new InvalidOperationException($"抖音加载页模板中找不到唯一的 {configName}.style。");

            Group body = block.Groups["body"];
            string updated = ReplaceSingle(body.Value,
                @"(\b" + Regex.Escape(property) + @"\s*:\s*)(?:'[^']*'|[^,\r\n]+)", value);
            return content.Substring(0, body.Index) + updated + content.Substring(body.Index + body.Length);
        }

        private static string ReplaceSingle(string content, string pattern, string value)
        {
            Match match = Regex.Match(content, pattern, RegexOptions.CultureInvariant);
            if (!match.Success || match.NextMatch().Success)
                throw new InvalidOperationException($"抖音加载页模板发生变化，找不到唯一配置：{pattern}");

            Group prefix = match.Groups[1];
            return content.Substring(0, prefix.Index + prefix.Length) + value
                + content.Substring(match.Index + match.Length);
        }
    }
}
