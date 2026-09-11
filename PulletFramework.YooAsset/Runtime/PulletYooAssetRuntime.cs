using System;
using System.Collections;
using UnityEngine;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    public enum EPulletYooAssetStartupStatus
    {
        None,
        Initializing,
        UpdatingManifest,
        Downloading,
        Succeeded,
        Failed
    }

    /// <summary>
    /// YooAsset 标准启动流水线。游戏入口只负责等待它完成；平台文件系统可按需替换。
    /// </summary>
    public static class PulletYooAssetRuntime
    {
        public delegate FileSystemParameters WebFileSystemFactoryDelegate(
            string packageName, IRemoteService remoteService);

        public static EPulletYooAssetStartupStatus Status { get; private set; }
        public static string Error { get; private set; }
        public static string PackageVersion { get; private set; }
        public static float Progress { get; private set; }
        public static ResourcePackage Package { get; private set; }
        public static WebFileSystemFactoryDelegate WebFileSystemFactory { get; set; }

        public static event Action<EPulletYooAssetStartupStatus, float, string> ProgressChanged;

        /// <summary>使用项目统一配置初始化 YooAsset，业务入口无需持有配置资产。</summary>
        public static IEnumerator Initialize()
        {
            PulletYooAssetSettings settings = PulletYooAssetSettingsData.Setting;
            if (settings == null)
            {
                Fail("未找到 YooAsset 配置：请通过 Pullets/Workspace 创建 PulletYooAssetSettings。");
                yield break;
            }

            yield return InitializeInternal(settings);
        }

        private static IEnumerator InitializeInternal(PulletYooAssetSettings settings)
        {
            if (Status == EPulletYooAssetStartupStatus.Initializing
                || Status == EPulletYooAssetStartupStatus.UpdatingManifest
                || Status == EPulletYooAssetStartupStatus.Downloading)
                throw new InvalidOperationException("YooAsset startup is already running.");

            Error = null;
            PackageVersion = null;
            SetStatus(EPulletYooAssetStartupStatus.Initializing, 0f, "正在初始化资源包");

            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();
            YooAssets.SetAsyncOperationMaxTimeSlice(settings.operationTimeSliceMilliseconds);

            if (!YooAssets.TryGetPackage(settings.packageName, out ResourcePackage package))
                package = YooAssets.CreatePackage(settings.packageName);
            Package = package;

            InitializePackageOperation initializeOperation;
            try
            {
                initializeOperation = CreateInitializeOperation(package, settings);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                yield break;
            }
            yield return initializeOperation;
            if (initializeOperation.Status != EOperationStatus.Succeeded)
            {
                Fail(initializeOperation.Error);
                yield break;
            }

            EPulletYooAssetPlayMode playMode = settings.GetRuntimePlayMode();
            SetStatus(EPulletYooAssetStartupStatus.UpdatingManifest, 0.15f, "正在检查资源版本");
            var versionOperation = package.RequestPackageVersionAsync(
                new RequestPackageVersionOptions(true, settings.timeoutSeconds));
            yield return versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeeded)
            {
                Fail(versionOperation.Error);
                yield break;
            }

            PackageVersion = versionOperation.PackageVersion;
            var manifestOperation = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(PackageVersion, settings.timeoutSeconds));
            yield return manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeeded)
            {
                Fail(manifestOperation.Error);
                yield break;
            }

            if ((playMode == EPulletYooAssetPlayMode.Host || playMode == EPulletYooAssetPlayMode.Web)
                && settings.downloadAllOnStartup)
            {
                var options = new ResourceDownloaderOptions(
                    settings.maximumConcurrency, settings.retryCount);
                ResourceDownloaderOperation downloader = package.CreateResourceDownloader(options);
                if (downloader.TotalDownloadCount > 0)
                {
                    SetStatus(EPulletYooAssetStartupStatus.Downloading, 0.25f,
                        $"准备下载 {downloader.TotalDownloadCount} 个资源文件");
                    downloader.DownloadProgressChanged += args =>
                    {
                        float progress = 0.25f + args.Progress * 0.7f;
                        SetStatus(EPulletYooAssetStartupStatus.Downloading, progress,
                            $"正在下载资源 {args.CurrentDownloadCount}/{args.TotalDownloadCount}");
                    };
                    downloader.StartDownload();
                    yield return downloader;
                    if (downloader.Status != EOperationStatus.Succeeded)
                    {
                        Fail(downloader.Error);
                        yield break;
                    }
                }
            }

            YooAssetResourceAdapter.Install(settings.packageName);
            SetStatus(EPulletYooAssetStartupStatus.Succeeded, 1f, "资源准备完成");
        }

        /// <summary>清理失败的启动状态并重新执行完整流程。</summary>
        public static IEnumerator RetryInitialize()
        {
            if (Status != EPulletYooAssetStartupStatus.Failed)
                throw new InvalidOperationException("Retry is only available after YooAsset startup failed.");

            Reset(true);
            yield return Initialize();
        }

        public static void Reset(bool destroyYooAssets)
        {
            PulletFramework.Resource.PulletResources.Uninstall();
            Package = null;
            PackageVersion = null;
            Error = null;
            Progress = 0f;
            Status = EPulletYooAssetStartupStatus.None;
            if (destroyYooAssets && YooAssets.IsInitialized)
                YooAssets.Destroy();
        }

        private static InitializePackageOperation CreateInitializeOperation(
            ResourcePackage package, PulletYooAssetSettings settings)
        {
            switch (settings.GetRuntimePlayMode())
            {
                case EPulletYooAssetPlayMode.EditorSimulate:
#if UNITY_EDITOR
                    var buildResult = EditorSimulateBuildInvoker.Build(
                        settings.packageName, (int)EBundleType.VirtualAssetBundle);
                    var editorOptions = new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters =
                            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                                buildResult.PackageRootDirectory)
                    };
                    return package.InitializePackageAsync(editorOptions);
#else
                    throw new InvalidOperationException("EditorSimulate mode is only available in the Unity Editor.");
#endif
                case EPulletYooAssetPlayMode.Offline:
                    var offlineOptions = new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                    };
                    return package.InitializePackageAsync(offlineOptions);
                case EPulletYooAssetPlayMode.Host:
                    IRemoteService hostRemote = CreateRemoteService(settings);
                    var hostOptions = new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                        CacheFileSystemParameters =
                            FileSystemParameters.CreateDefaultSandboxFileSystemParameters(hostRemote)
                    };
                    hostOptions.CacheFileSystemParameters.AddParameter(
                        EFileSystemParameter.DownloadMaxConcurrency, settings.maximumConcurrency);
                    return package.InitializePackageAsync(hostOptions);
                case EPulletYooAssetPlayMode.Web:
                    IRemoteService webRemote = CreateRemoteService(settings);
                    FileSystemParameters fileSystem = WebFileSystemFactory != null
                        ? WebFileSystemFactory(settings.packageName, webRemote)
                        : FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(webRemote, true);
                    var webOptions = new WebPlayModeOptions
                    {
                        WebNetworkFileSystemParameters = fileSystem
                    };
                    return package.InitializePackageAsync(webOptions);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static IRemoteService CreateRemoteService(PulletYooAssetSettings settings)
        {
            string primary = settings.ResolveDefaultHostServer();
            string fallback = settings.ResolveFallbackHostServer();
            if (string.IsNullOrWhiteSpace(primary))
                throw new InvalidOperationException(
                    "YooAsset remote play mode requires a business asset CDN URL.");
            return new YooAssetRemoteService(primary, fallback);
        }

        private static void Fail(string error)
        {
            Error = string.IsNullOrWhiteSpace(error) ? "Unknown YooAsset startup error." : error;
            SetStatus(EPulletYooAssetStartupStatus.Failed, Progress, Error);
            Debug.LogError($"[PulletYooAsset] {Error}");
        }

        private static void SetStatus(
            EPulletYooAssetStartupStatus status, float progress, string message)
        {
            Status = status;
            Progress = Mathf.Clamp01(progress);
            ProgressChanged?.Invoke(status, Progress, message);
        }
    }
}
