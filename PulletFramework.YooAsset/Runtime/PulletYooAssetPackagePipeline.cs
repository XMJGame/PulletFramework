using System;
using System.Collections;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    internal readonly struct PulletYooAssetPipelineRequest
    {
        public string PackageName { get; }
        public EPulletYooAssetOperationType OperationType { get; }
        public string TargetVersion { get; }
        public bool Download { get; }
        public bool ClearUnusedCache { get; }
        public string[] Tags { get; }

        public PulletYooAssetPipelineRequest(
            string packageName,
            EPulletYooAssetOperationType operationType,
            string targetVersion = null,
            bool download = false,
            bool clearUnusedCache = false,
            string[] tags = null)
        {
            PackageName = packageName;
            OperationType = operationType;
            TargetVersion = targetVersion;
            Download = download;
            ClearUnusedCache = clearUnusedCache;
            Tags = tags;
        }

        public PulletYooAssetPipelineRequest WithPackageName(string packageName)
        {
            return new PulletYooAssetPipelineRequest(
                packageName, OperationType, TargetVersion, Download, ClearUnusedCache, Tags);
        }
    }

    /// <summary>执行单个 Package 的初始化、更新、下载、清理和卸载流水线。</summary>
    internal static class PulletYooAssetPackagePipeline
    {
        internal static IEnumerator Run(
            PulletYooAssetPipelineRequest request,
            PulletYooAssetSettings settings,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished)
        {
            state.Error = null;
            state.Progress = 0f;

            if (request.OperationType == EPulletYooAssetOperationType.Unload)
            {
                yield return DestroyPackage(request, state, operation, finished);
                yield break;
            }

            ResourcePackage package;
            try
            {
                package = PulletYooAssetPackageInitializer.EnsurePackage(
                    settings, request.PackageName);
            }
            catch (Exception exception)
            {
                Fail(state, operation, finished, exception.Message);
                yield break;
            }

            if (package.InitializeStatus != EOperationStatus.Succeeded)
            {
                SetState(state, operation, EPulletYooAssetPackageStatus.Initializing, 0.02f);
                InitializePackageOperation initializeOperation;
                try
                {
                    initializeOperation = PulletYooAssetPackageInitializer.CreateOperation(
                        package, settings, request.PackageName);
                }
                catch (Exception exception)
                {
                    Fail(state, operation, finished, exception.Message);
                    yield break;
                }

                yield return initializeOperation;
                if (!CheckResult(state, operation, finished,
                        initializeOperation.Status, initializeOperation.Error))
                    yield break;
            }

            state.CurrentVersion = TryGetActiveVersion(package);
            if (operation.CancellationRequested)
            {
                Cancel(state, operation, finished);
                yield break;
            }

            if (request.OperationType == EPulletYooAssetOperationType.Initialize)
            {
                CompleteReadyState(state, operation, finished);
                yield break;
            }

            if (request.OperationType == EPulletYooAssetOperationType.UnloadUnusedAssets)
            {
                SetState(state, operation, EPulletYooAssetPackageStatus.Unloading, 0.25f);
                UnloadUnusedAssetsOperation unloadOperation = package.UnloadUnusedAssetsAsync();
                yield return unloadOperation;
                if (!CheckResult(state, operation, finished,
                        unloadOperation.Status, unloadOperation.Error))
                    yield break;
                CompleteReadyState(state, operation, finished);
                yield break;
            }

            if (request.OperationType == EPulletYooAssetOperationType.ClearUnusedCache)
            {
                if (string.IsNullOrWhiteSpace(state.CurrentVersion))
                {
                    Fail(state, operation, finished,
                        $"资源包 {request.PackageName} 尚未加载活动清单，无法清理未使用缓存。");
                    yield break;
                }
                yield return ClearUnusedCache(package, state, operation, finished, 0.2f);
                if (!operation.IsDone)
                    CompleteReadyState(state, operation, finished);
                yield break;
            }

            string targetVersion = request.TargetVersion;
            SetState(state, operation, EPulletYooAssetPackageStatus.CheckingVersion, 0.12f);
            if (string.IsNullOrWhiteSpace(targetVersion))
            {
                RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync(
                    new RequestPackageVersionOptions(true, settings.timeoutSeconds));
                yield return versionOperation;
                if (!CheckResult(state, operation, finished,
                        versionOperation.Status, versionOperation.Error))
                    yield break;
                targetVersion = versionOperation.PackageVersion;
            }

            state.RemoteVersion = targetVersion;
            state.HasUpdate = !string.Equals(
                state.CurrentVersion, targetVersion, StringComparison.Ordinal);
            operation.PackageVersion = targetVersion;

            if (request.OperationType == EPulletYooAssetOperationType.Check)
            {
                CompleteReadyState(state, operation, finished);
                yield break;
            }

            SetState(state, operation, EPulletYooAssetPackageStatus.UpdatingManifest, 0.2f);
            LoadPackageManifestOperation manifestOperation = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(targetVersion, settings.timeoutSeconds));
            yield return manifestOperation;
            if (!CheckResult(state, operation, finished,
                    manifestOperation.Status, manifestOperation.Error))
                yield break;
            state.CurrentVersion = targetVersion;
            state.HasUpdate = false;

            if (operation.CancellationRequested)
            {
                Cancel(state, operation, finished);
                yield break;
            }

            if (request.Download)
            {
                ResourceDownloaderOptions downloaderOptions =
                    request.Tags != null && request.Tags.Length > 0
                        ? new ResourceDownloaderOptions(
                            request.Tags, settings.maximumConcurrency, settings.retryCount)
                        : new ResourceDownloaderOptions(
                            settings.maximumConcurrency, settings.retryCount);
                ResourceDownloaderOperation downloader =
                    package.CreateResourceDownloader(downloaderOptions);
                BindDownloader(downloader, state, operation);
                if (downloader.TotalDownloadCount > 0)
                {
                    SetState(state, operation, EPulletYooAssetPackageStatus.Downloading, 0.25f);
                    downloader.StartDownload();
                    yield return downloader;
                    if (!CheckResult(state, operation, finished, downloader.Status, downloader.Error))
                        yield break;
                }
            }

            if (request.ClearUnusedCache)
            {
                yield return ClearUnusedCache(package, state, operation, finished, 0.92f);
                if (operation.IsDone)
                    yield break;
            }

            CompleteReadyState(state, operation, finished);
        }

        private static IEnumerator DestroyPackage(
            PulletYooAssetPipelineRequest request,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished)
        {
            SetState(state, operation, EPulletYooAssetPackageStatus.Unloading, 0f);
            if (YooAssets.IsInitialized
                && YooAssets.TryGetPackage(request.PackageName, out ResourcePackage package))
            {
                DestroyPackageOperation destroyOperation = package.DestroyPackageAsync();
                yield return destroyOperation;
                if (!CheckResult(state, operation, finished,
                        destroyOperation.Status, destroyOperation.Error))
                    yield break;
                YooAssets.RemovePackage(request.PackageName);
            }
            operation.Complete(null);
            finished(true);
        }

        private static IEnumerator ClearUnusedCache(
            ResourcePackage package,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            float progress)
        {
            SetState(state, operation, EPulletYooAssetPackageStatus.ClearingCache, progress);
            ClearCacheOperation clearOperation = package.ClearCacheAsync(
                new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles));
            yield return clearOperation;
            CheckResult(state, operation, finished, clearOperation.Status, clearOperation.Error);
        }

        private static void BindDownloader(
            ResourceDownloaderOperation downloader,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation)
        {
            operation.BindDownloader(downloader);
            operation.TotalDownloadCount = downloader.TotalDownloadCount;
            operation.TotalDownloadBytes = downloader.TotalDownloadBytes;
            state.TotalDownloadCount = downloader.TotalDownloadCount;
            state.TotalDownloadBytes = downloader.TotalDownloadBytes;
            downloader.DownloadFileStarted += args =>
                operation.ReportDownloadFileStarted(args.FileName, args.FileSize);
            downloader.DownloadError += args =>
                operation.ReportDownloadError(args.FileName, args.ErrorInfo);
            downloader.DownloadProgressChanged += args =>
            {
                operation.CurrentDownloadCount = args.CurrentDownloadCount;
                operation.CurrentDownloadBytes = args.CurrentDownloadBytes;
                state.CurrentDownloadCount = args.CurrentDownloadCount;
                state.CurrentDownloadBytes = args.CurrentDownloadBytes;
                float progress = 0.25f + args.Progress * 0.65f;
                state.Progress = progress;
                operation.ReportProgress(progress);
            };
        }

        private static string TryGetActiveVersion(ResourcePackage package)
        {
            try
            {
                return package.GetPackageVersion();
            }
            catch (YooPackageInvalidException)
            {
                return null;
            }
        }

        private static bool CheckResult(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            EOperationStatus status,
            string error)
        {
            if (operation.CancellationRequested)
            {
                Cancel(state, operation, finished);
                return false;
            }
            if (status == EOperationStatus.Succeeded)
                return true;
            Fail(state, operation, finished, error);
            return false;
        }

        private static void CompleteReadyState(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished)
        {
            SetState(state, operation,
                string.IsNullOrWhiteSpace(state.CurrentVersion)
                    ? EPulletYooAssetPackageStatus.Initialized
                    : EPulletYooAssetPackageStatus.Ready,
                1f);
            operation.Complete(state.CurrentVersion);
            finished(false);
        }

        private static void Cancel(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished)
        {
            state.Error = "操作已取消。";
            state.Status = EPulletYooAssetPackageStatus.Failed;
            operation.Fail(state.Error, true);
            finished(false);
        }

        private static void Fail(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            string error)
        {
            state.Error = string.IsNullOrWhiteSpace(error)
                ? "Unknown YooAsset operation error."
                : error;
            state.Status = EPulletYooAssetPackageStatus.Failed;
            operation.Fail(state.Error);
            PLogger.Error($"[PulletYooAsset] {operation.PackageName}: {state.Error}");
            finished(false);
        }

        private static void SetState(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            EPulletYooAssetPackageStatus status,
            float progress)
        {
            state.Status = status;
            state.Progress = UnityEngine.Mathf.Clamp01(progress);
            operation.ReportProgress(state.Progress);
        }
    }
}
