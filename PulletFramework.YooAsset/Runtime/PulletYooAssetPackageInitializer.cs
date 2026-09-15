using System;
using PulletFramework.Resource;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>隔离运行模式、文件系统和远端地址的创建逻辑。</summary>
    internal static class PulletYooAssetPackageInitializer
    {
        internal static PulletYooAssets.WebFileSystemFactoryDelegate WebFileSystemFactory { get; set; }

        internal static ResourcePackage EnsurePackage(
            PulletYooAssetSettings settings, string packageName)
        {
            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();
            YooAssets.SetAsyncOperationMaxTimeSlice(settings.operationTimeSliceMilliseconds);
            YooAssetResourceAdapter.Install(settings.packageName);
            return YooAssets.TryGetPackage(packageName, out ResourcePackage package)
                ? package
                : YooAssets.CreatePackage(packageName);
        }

        internal static InitializePackageOperation CreateOperation(
            ResourcePackage package,
            PulletYooAssetSettings settings,
            string packageName)
        {
            switch (settings.GetRuntimePlayMode())
            {
                case EPulletYooAssetPlayMode.EditorSimulate:
                    return CreateEditorSimulateOperation(package, packageName);
                case EPulletYooAssetPlayMode.Offline:
                    return package.InitializePackageAsync(new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                    });
                case EPulletYooAssetPlayMode.Host:
                    return CreateHostOperation(package, settings, packageName);
                case EPulletYooAssetPlayMode.Web:
                    return CreateWebOperation(package, settings, packageName);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal static InitializePackageOperation CreateBuiltinOnlyWebOperation(
            ResourcePackage package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            return package.InitializePackageAsync(new WebPlayModeOptions
            {
                WebServerFileSystemParameters =
                    FileSystemParameters.CreateDefaultWebServerFileSystemParameters(true)
            });
        }

        private static InitializePackageOperation CreateEditorSimulateOperation(
            ResourcePackage package, string packageName)
        {
#if UNITY_EDITOR
            var buildResult = EditorSimulateBuildInvoker.Build(
                packageName, (int)EBundleType.VirtualAssetBundle);
            return package.InitializePackageAsync(new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                        buildResult.PackageRootDirectory)
            });
#else
            throw new InvalidOperationException(
                "EditorSimulate mode is only available in the Unity Editor.");
#endif
        }

        private static InitializePackageOperation CreateHostOperation(
            ResourcePackage package,
            PulletYooAssetSettings settings,
            string packageName)
        {
            IRemoteService remoteService = CreateRemoteService(settings, packageName);
            FileSystemParameters builtin =
                FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            builtin.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
            FileSystemParameters cache =
                FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            cache.AddParameter(
                EFileSystemParameter.DownloadMaxConcurrency, settings.maximumConcurrency);
            cache.AddParameter(EFileSystemParameter.DownloadMaxRequestPerFrame, 1);
            cache.AddParameter(
                EFileSystemParameter.DownloadWatchdogTimeout, settings.timeoutSeconds);
            return package.InitializePackageAsync(new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = builtin,
                CacheFileSystemParameters = cache
            });
        }

        private static InitializePackageOperation CreateWebOperation(
            ResourcePackage package,
            PulletYooAssetSettings settings,
            string packageName)
        {
            IRemoteService remoteService = CreateRemoteService(settings, packageName);
            var options = new WebPlayModeOptions();
            if (settings.includeDefaultPackageInStreamingAssets
                && string.Equals(packageName, settings.packageName, StringComparison.Ordinal))
            {
                // WebServerFileSystem represents files shipped in StreamingAssets and is
                // registered first so YooAsset prefers built-in bundles over remote files.
                options.WebServerFileSystemParameters =
                    FileSystemParameters.CreateDefaultWebServerFileSystemParameters(true);
            }
            if (WebFileSystemFactory != null)
            {
                // 小游戏平台文件系统同时负责下载与持久缓存。
                options.WebNetworkFileSystemParameters =
                    WebFileSystemFactory(packageName, remoteService);
            }
            else
            {
                // 标准 WebGL 同时支持 StreamingAssets 和远端 CDN。
                options.WebServerFileSystemParameters =
                    FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                options.WebNetworkFileSystemParameters =
                    FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(
                        remoteService, true);
            }
            return package.InitializePackageAsync(options);
        }

        private static IRemoteService CreateRemoteService(
            PulletYooAssetSettings settings, string packageName)
        {
            string primary = settings.ResolveDefaultHostServer(packageName);
            string fallback = settings.ResolveFallbackHostServer(packageName);
            if (string.IsNullOrWhiteSpace(primary))
                throw new InvalidOperationException(
                    $"资源包 {packageName} 使用远端模式，但没有配置业务资源 CDN 地址。");
            return new YooAssetRemoteService(primary, fallback);
        }
    }
}
