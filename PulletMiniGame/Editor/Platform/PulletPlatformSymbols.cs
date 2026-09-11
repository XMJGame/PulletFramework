using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace PulletMiniGame.Editor
{
    public static class PulletPlatformSymbols
    {
        public const string MiniGame = "PULLET_MINIGAME";
        public const string WeChat = "PULLET_PLATFORM_WECHAT";
        public const string Douyin = "PULLET_PLATFORM_DOUYIN";
        public const string Development = "PULLET_ENV_DEVELOPMENT";
        public const string Test = "PULLET_ENV_TEST";
        public const string Release = "PULLET_ENV_RELEASE";
        private const string DouyinMixEngine = "TTSDK_MIX_ENGINE";
        private const string DouyinSubplatform = "MINIGAME_SUBPLATFORM_DOUYIN";
        private const string WeChatSubplatform = "MINIGAME_SUBPLATFORM_WEIXIN";

        private static readonly HashSet<string> OwnedSymbols = new HashSet<string>
        {
            MiniGame, Development, Test, Release,
            DouyinMixEngine, DouyinSubplatform, WeChatSubplatform
        };

        public static void Apply(string platformId, EMiniGameBuildEnvironment environment)
        {
            var symbols = GetSymbols();
            symbols.RemoveAll(IsOwnedSymbol);
            symbols.Add(MiniGame);
            symbols.Add(GetPlatformSymbol(platformId));
            symbols.Add(GetEnvironmentSymbol(environment));
            if (string.Equals(platformId, "douyin", StringComparison.OrdinalIgnoreCase))
                symbols.Add(DouyinMixEngine);
            SetSymbols(symbols.Distinct(StringComparer.Ordinal).OrderBy(value => value));
        }

        public static bool IsApplied(string platformId, EMiniGameBuildEnvironment environment)
        {
            List<string> symbols = GetSymbols();
            string platformSymbol = GetPlatformSymbol(platformId);
            return symbols.Contains(MiniGame)
                && symbols.Contains(platformSymbol)
                && symbols.Contains(GetEnvironmentSymbol(environment));
        }

        public static string GetPlatformSymbol(string platformId)
        {
            if (string.IsNullOrWhiteSpace(platformId))
                throw new ArgumentException("Platform id is required.", nameof(platformId));

            string suffix = new string(platformId.Trim().ToUpperInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray());
            return $"PULLET_PLATFORM_{suffix}";
        }

        private static bool IsOwnedSymbol(string symbol)
        {
            return OwnedSymbols.Contains(symbol)
                || symbol.StartsWith("PULLET_PLATFORM_", StringComparison.Ordinal);
        }

        private static string GetEnvironmentSymbol(EMiniGameBuildEnvironment environment)
        {
            switch (environment)
            {
                case EMiniGameBuildEnvironment.Test:
                    return Test;
                case EMiniGameBuildEnvironment.Release:
                    return Release;
                default:
                    return Development;
            }
        }

        private static List<string> GetSymbols()
        {
#if UNITY_2021_2_OR_NEWER
            string value = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
#else
            string value = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL);
#endif
            return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static void SetSymbols(IEnumerable<string> symbols)
        {
            string value = string.Join(";", symbols);
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, value);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL, value);
#endif
        }
    }
}
