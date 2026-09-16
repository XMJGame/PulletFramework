using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.MiniGame.Platform;
using UnityEngine;

namespace PulletFramework.MiniGame
{
    public static class MiniGameBootstrap
    {
        private static readonly Dictionary<string, Func<IPlatformAdapter>> Factories =
            new Dictionary<string, Func<IPlatformAdapter>>(StringComparer.OrdinalIgnoreCase);

        // SDK samples register before the first scene; no reflection is needed in the player.
        public static void Register(string platformId, Func<IPlatformAdapter> factory)
        {
            if (string.IsNullOrWhiteSpace(platformId))
                throw new ArgumentException("Platform id is required.", nameof(platformId));
            Factories[platformId] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static IPlatformAdapter CreateAdapter(string platformId = null)
        {
            if (platformId == null)
            {
                if (Application.isEditor)
                    platformId = PulletPlatformIds.Editor;
                else
                {
                    foreach (string id in Factories.Keys)
                    {
                        if (id == PulletPlatformIds.Editor)
                            continue;
                        if (platformId != null)
                            throw new InvalidOperationException("Multiple player platforms registered. Check platform symbols.");
                        platformId = id;
                    }
                }
            }

            if (platformId == null || !Factories.TryGetValue(platformId, out var factory))
                throw new InvalidOperationException(
                    $"No runtime factory for '{platformId ?? "selected platform"}'. Install its SDK bridge and apply platform symbols.");

            IPlatformAdapter adapter = factory();
            if (adapter == null || !string.Equals(adapter.Id, platformId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Factory '{platformId}' returned an invalid adapter.");
            if (adapter is ConfiguredPlatformAdapter)
                return adapter;
            var settings = MiniGameRuntimeSettings.Load(platformId);
            return settings == null ? adapter : new ConfiguredPlatformAdapter(adapter, settings);
        }

        /// <summary>安装并初始化当前宏选中的小游戏平台。</summary>
        public static Task<PlatformResult> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            if (PulletMiniGames.IsInstalled)
                return PulletMiniGames.InitializeAsync(PulletPlatform.Adapter, cancellationToken);

            IPlatformAdapter adapter;
            try { adapter = CreateAdapter(); }
            catch (Exception exception)
            {
                return PlatformTask.FromResult(
                    PlatformResult.Failure(exception.Message), cancellationToken);
            }
            return PulletMiniGames.InitializeAsync(adapter, cancellationToken);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            PulletMiniGames.Shutdown();
            Factories.Clear();
        }
    }
}
