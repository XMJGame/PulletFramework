using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    public sealed class PlatformBuildContext
    {
        public string OutputPath { get; }
        public bool DevelopmentBuild { get; }
        public bool CleanOutput { get; }
        public ScriptableObject Settings { get; }

        public PlatformBuildContext(
            string outputPath,
            bool developmentBuild = false,
            bool cleanOutput = true,
            ScriptableObject settings = null)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            OutputPath = outputPath;
            DevelopmentBuild = developmentBuild;
            CleanOutput = cleanOutput;
            Settings = settings;
        }
    }

    public interface IPlatformBuildAdapter
    {
        string Id { get; }
        string DisplayName { get; }
        string ActionLabel { get; }
        bool IsAvailable(out string reason);
        void Export(PlatformBuildContext context);
    }

    public static class PulletPlatformBuild
    {
        private static readonly Dictionary<string, IPlatformBuildAdapter> Adapters =
            new Dictionary<string, IPlatformBuildAdapter>(StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<IPlatformBuildAdapter> All => Adapters.Values;

        public static void Register(IPlatformBuildAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (string.IsNullOrWhiteSpace(adapter.Id))
                throw new ArgumentException("Platform build adapter id is required.", nameof(adapter));

            Adapters[adapter.Id] = adapter;
        }

        public static void Unregister(string platformId)
        {
            if (!string.IsNullOrWhiteSpace(platformId))
                Adapters.Remove(platformId);
        }

        public static bool TryGet(string platformId, out IPlatformBuildAdapter adapter)
        {
            adapter = null;
            return !string.IsNullOrWhiteSpace(platformId)
                && Adapters.TryGetValue(platformId, out adapter);
        }

        public static void Export(string platformId, PlatformBuildContext context)
        {
            if (!TryGet(platformId, out IPlatformBuildAdapter adapter))
                throw new InvalidOperationException($"Platform build adapter '{platformId}' is not registered.");
            if (!adapter.IsAvailable(out string reason))
                throw new InvalidOperationException(
                    $"Platform build adapter '{platformId}' is unavailable: {reason}");

            if (context?.Settings is MiniGamePlatformSettings settings)
            {
                MiniGameFirstPackageCdnPublisher.PrepareBuildSettings(
                    platformId, MiniGameBuildSettingsData.Common.version, settings);
            }

            adapter.Export(context ?? throw new ArgumentNullException(nameof(context)));
        }
    }
}
