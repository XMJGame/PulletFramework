using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PulletMiniGame.Editor
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
    }
}
