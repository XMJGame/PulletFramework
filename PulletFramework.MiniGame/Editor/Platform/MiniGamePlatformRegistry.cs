using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    public interface IMiniGamePlatformDefinition
    {
        string Id { get; }
        string DisplayName { get; }
        int Order { get; }
        MiniGamePlatformSettings LoadSettings();
        void DrawAdditionalSettings(MiniGamePlatformSettings settings);
        bool Validate(MiniGamePlatformSettings settings, out string error);
    }

    public interface IMiniGamePlatformDiagnostics
    {
        void DrawDiagnostics();
    }

    public static class MiniGamePlatformRegistry
    {
        private static IReadOnlyList<IMiniGamePlatformDefinition> _definitions;

        public static IReadOnlyList<IMiniGamePlatformDefinition> All =>
            _definitions ??= Discover();

        public static bool TryGet(string platformId, out IMiniGamePlatformDefinition definition)
        {
            definition = All.FirstOrDefault(item =>
                string.Equals(item.Id, platformId, StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        public static void Refresh()
        {
            _definitions = Discover();
        }

        private static IReadOnlyList<IMiniGamePlatformDefinition> Discover()
        {
            var definitions = new List<IMiniGamePlatformDefinition>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IMiniGamePlatformDefinition>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    definitions.Add((IMiniGamePlatformDefinition)Activator.CreateInstance(type));
                }
                catch (Exception exception)
                {
                    PulletFramework.PLogger.EditorException(
                        exception, "[PulletFramework.MiniGame] 平台定义发现失败。");
                }
            }

            return definitions
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Order)
                .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
                .ToArray();
        }
    }

}
