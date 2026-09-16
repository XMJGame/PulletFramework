using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
            Tags = NormalizeTags(tags);
        }

        public PulletYooAssetPipelineRequest WithPackageName(string packageName)
        {
            return new PulletYooAssetPipelineRequest(
                packageName, OperationType, TargetVersion, Download, ClearUnusedCache, Tags);
        }

        private static string[] NormalizeTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return Array.Empty<string>();

            var result = new List<string>(tags.Length);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < tags.Length; index++)
            {
                string tag = tags[index]?.Trim();
                if (!string.IsNullOrEmpty(tag) && unique.Add(tag))
                    result.Add(tag);
            }
            return result.ToArray();
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
            state.TotalDownloadCount = 0;
            state.TotalDownloadBytes = 0;
            state.CurrentDownloadCount = 0;
            state.CurrentDownloadBytes = 0;
            state.UsedBuiltinFallback = false;
            state.FallbackReason = null;

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
            bool explicitTargetVersion = !string.IsNullOrWhiteSpace(targetVersion);
            bool reuseCurrentManifest = false;
            bool numericVersionPolicy = settings.GetRuntimePlayMode()
                != EPulletYooAssetPlayMode.EditorSimulate;
            SetState(state, operation, EPulletYooAssetPackageStatus.CheckingVersion, 0.12f);
            if (string.IsNullOrWhiteSpace(targetVersion))
            {
                RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync(
                    new RequestPackageVersionOptions(true, settings.timeoutSeconds));
                yield return versionOperation;
                if (versionOperation.Status != EOperationStatus.Succeeded)
                {
                    if (CanReuseCurrentManifest(request, state))
                    {
                        targetVersion = state.CurrentVersion;
                        reuseCurrentManifest = true;
                        state.RemoteVersion = null;
                        state.HasUpdate = false;
                        PLogger.Warning(
                            $"[PulletYooAsset] {request.PackageName}: 远端版本检查失败，"
                            + $"继续使用当前版本 {state.CurrentVersion}。原因："
                            + versionOperation.Error);
                    }
                    else if (CanFallbackToBuiltin(request, settings, state, operation))
                    {
                        yield return PrepareBuiltinFallback(
                            package, request, settings, state, operation, finished,
                            versionOperation.Error);
                        yield break;
                    }
                    else
                    {
                        CheckResult(state, operation, finished,
                            versionOperation.Status, versionOperation.Error);
                        yield break;
                    }
                }
                else
                {
                    targetVersion = versionOperation.PackageVersion;
                }
            }

            if (!reuseCurrentManifest)
                state.RemoteVersion = targetVersion;
            if (numericVersionPolicy && explicitTargetVersion
                && !PulletYooAssetVersion.IsValid(targetVersion))
            {
                Fail(state, operation, finished,
                    $"指定资源版本 {targetVersion} 不是支持的数字分段格式。");
                yield break;
            }
            if (numericVersionPolicy && !explicitTargetVersion && !reuseCurrentManifest)
            {
                string builtinVersion = CanFallbackToBuiltin(request, settings, state, operation)
                    ? settings.builtinDefaultPackageVersion
                    : null;
                EPulletYooAssetAutomaticVersionDecision decision =
                    PulletYooAssetVersion.SelectAutomatic(
                        state.CurrentVersion, builtinVersion, targetVersion,
                        out string versionReason);
                if (decision == EPulletYooAssetAutomaticVersionDecision.Incomparable)
                {
                    Fail(state, operation, finished,
                        versionReason + "请统一该资源通道的版本格式，或显式指定目标版本。");
                    yield break;
                }
                if (decision == EPulletYooAssetAutomaticVersionDecision.PreferBuiltin)
                {
                    yield return PrepareBuiltinFallback(
                        package, request, settings, state, operation, finished, versionReason);
                    yield break;
                }
                if (decision == EPulletYooAssetAutomaticVersionDecision.KeepCurrent)
                {
                    state.HasUpdate = false;
                    if (request.OperationType == EPulletYooAssetOperationType.Check
                        || request.OperationType == EPulletYooAssetOperationType.Update)
                    {
                        CompleteReadyState(state, operation, finished,
                            request.OperationType == EPulletYooAssetOperationType.Check
                                ? targetVersion : null);
                        yield break;
                    }
                    targetVersion = state.CurrentVersion;
                    reuseCurrentManifest = true;
                }
            }
            state.HasUpdate = !string.Equals(
                state.CurrentVersion, targetVersion, StringComparison.Ordinal);
            operation.PackageVersion = targetVersion;

            if (request.OperationType == EPulletYooAssetOperationType.Check)
            {
                CompleteReadyState(state, operation, finished, targetVersion);
                yield break;
            }

            if (!reuseCurrentManifest)
            {
                SetState(state, operation, EPulletYooAssetPackageStatus.UpdatingManifest, 0.2f);
                LoadPackageManifestOperation manifestOperation = package.LoadPackageManifestAsync(
                    new LoadPackageManifestOptions(targetVersion, settings.timeoutSeconds));
                yield return manifestOperation;
                if (manifestOperation.Status != EOperationStatus.Succeeded)
                {
                    if (!explicitTargetVersion && CanReuseCurrentManifest(request, state))
                    {
                        PLogger.Warning(
                            $"[PulletYooAsset] {request.PackageName}: 远端清单 {targetVersion} "
                            + $"加载失败，继续使用当前版本 {state.CurrentVersion}。原因："
                            + manifestOperation.Error);
                        targetVersion = state.CurrentVersion;
                        operation.PackageVersion = targetVersion;
                    }
                    else if (CanFallbackToBuiltin(request, settings, state, operation))
                    {
                        yield return PrepareBuiltinFallback(
                            package, request, settings, state, operation, finished,
                            manifestOperation.Error);
                        yield break;
                    }
                    else
                    {
                        CheckResult(state, operation, finished,
                            manifestOperation.Status, manifestOperation.Error);
                        yield break;
                    }
                }
                else
                {
                    state.CurrentVersion = targetVersion;
                }
            }
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
                    if (downloader.Status != EOperationStatus.Succeeded)
                    {
                        if (CanFallbackToBuiltin(request, settings, state, operation))
                        {
                            yield return PrepareBuiltinFallback(
                                package, request, settings, state, operation, finished,
                                downloader.Error);
                            yield break;
                        }
                        CheckResult(state, operation, finished,
                            downloader.Status, downloader.Error);
                        yield break;
                    }
                }
            }

            if (request.OperationType == EPulletYooAssetOperationType.Prepare)
            {
                yield return WarmupShaderVariants(
                    package, request, settings, state, operation, finished);
                if (operation.IsDone)
                    yield break;
            }

            if (request.ClearUnusedCache)
            {
                yield return ClearUnusedCache(package, state, operation, finished, 0.97f);
                if (operation.IsDone)
                    yield break;
            }

            CompleteReadyState(state, operation, finished);
        }

        internal static bool CanFallbackToBuiltin(
            PulletYooAssetPipelineRequest request,
            PulletYooAssetSettings settings,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation)
        {
            if (operation.CancellationRequested
                || request.OperationType != EPulletYooAssetOperationType.Prepare
                || !settings.includeDefaultPackageInStreamingAssets
                || !string.Equals(request.PackageName, settings.packageName, StringComparison.Ordinal)
                || settings.GetRuntimePlayMode() != EPulletYooAssetPlayMode.Web)
                return false;

            if (string.IsNullOrWhiteSpace(state.CurrentVersion))
                return true;
            return !string.IsNullOrWhiteSpace(settings.builtinDefaultPackageVersion)
                && PulletYooAssetVersion.TryCompare(
                    settings.builtinDefaultPackageVersion, state.CurrentVersion,
                    out int comparison) && comparison >= 0;
        }

        internal static bool CanReuseCurrentManifest(
            PulletYooAssetPipelineRequest request, PulletYooAssetPackageState state)
        {
            return request.OperationType == EPulletYooAssetOperationType.Prepare
                && !string.IsNullOrWhiteSpace(state.CurrentVersion);
        }

        private static IEnumerator PrepareBuiltinFallback(
            ResourcePackage package,
            PulletYooAssetPipelineRequest request,
            PulletYooAssetSettings settings,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            string remoteError)
        {
            string reason = string.IsNullOrWhiteSpace(remoteError)
                ? "远端资源服务不可用。"
                : remoteError;
            state.UsedBuiltinFallback = true;
            state.FallbackReason = reason;
            state.Error = null;
            state.RemoteVersion = null;
            state.HasUpdate = false;
            state.TotalDownloadCount = 0;
            state.TotalDownloadBytes = 0;
            state.CurrentDownloadCount = 0;
            state.CurrentDownloadBytes = 0;
            operation.MarkBuiltinFallback(reason);
            SetState(state, operation, EPulletYooAssetPackageStatus.FallingBackToBuiltin,
                UnityEngine.Mathf.Max(state.Progress, 0.3f));
            PLogger.Warning(
                $"[PulletYooAsset] {request.PackageName}: 远端资源不可用或不适用，" +
                $"尝试使用随包内置版本。原因：{reason}");

            DestroyPackageOperation destroyOperation = package.DestroyPackageAsync();
            yield return destroyOperation;
            if (!CheckFallbackResult(
                    state, operation, finished, destroyOperation.Status,
                    destroyOperation.Error, reason, "销毁远端资源包"))
                yield break;
            YooAssets.RemovePackage(request.PackageName);

            ResourcePackage builtinPackage;
            InitializePackageOperation initializeOperation;
            try
            {
                builtinPackage = PulletYooAssetPackageInitializer.EnsurePackage(
                    settings, request.PackageName);
                initializeOperation =
                    PulletYooAssetPackageInitializer.CreateBuiltinOnlyWebOperation(builtinPackage);
            }
            catch (Exception exception)
            {
                FailBuiltinFallback(
                    state, operation, finished, reason, "创建内置资源包", exception.Message);
                yield break;
            }

            yield return initializeOperation;
            if (!CheckFallbackResult(
                    state, operation, finished, initializeOperation.Status,
                    initializeOperation.Error, reason, "初始化内置文件系统"))
                yield break;

            RequestPackageVersionOperation versionOperation =
                builtinPackage.RequestPackageVersionAsync(
                    new RequestPackageVersionOptions(false, settings.timeoutSeconds));
            yield return versionOperation;
            if (!CheckFallbackResult(
                    state, operation, finished, versionOperation.Status,
                    versionOperation.Error, reason, "读取内置资源版本"))
                yield break;

            string builtinVersion = versionOperation.PackageVersion;
            LoadPackageManifestOperation manifestOperation =
                builtinPackage.LoadPackageManifestAsync(
                    new LoadPackageManifestOptions(builtinVersion, settings.timeoutSeconds));
            yield return manifestOperation;
            if (!CheckFallbackResult(
                    state, operation, finished, manifestOperation.Status,
                    manifestOperation.Error, reason, "加载内置资源清单"))
                yield break;

            state.CurrentVersion = builtinVersion;
            operation.PackageVersion = builtinVersion;
            yield return WarmupShaderVariants(
                builtinPackage, request, settings, state, operation, finished);
            if (operation.IsDone)
                yield break;
            PLogger.Warning(
                $"[PulletYooAsset] {request.PackageName}: 已降级使用随包内置版本 {builtinVersion}。" +
                "本次会话不再检查远端更新。");
            CompleteReadyState(state, operation, finished);
        }

        private static IEnumerator WarmupShaderVariants(
            ResourcePackage package,
            PulletYooAssetPipelineRequest request,
            PulletYooAssetSettings settings,
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished)
        {
            if (!settings.warmupShaderVariantsOnPrepare)
                yield break;

            string location = PulletYooAssetShaderVariants.GetLocation(settings, request.PackageName);
            if (!package.IsLocationValid(location))
            {
                PLogger.Warning(
                    $"[PulletYooAsset] {request.PackageName}: 未找到着色器变体集合 {location}，" +
                    "跳过预热。请重新构建该 Package。");
                yield break;
            }

            SetState(state, operation, EPulletYooAssetPackageStatus.WarmingShaderVariants, 0.92f);
            AssetHandle handle = package.LoadAssetAsync<ShaderVariantCollection>(location);
            yield return handle;
            if (handle.Status != EOperationStatus.Succeeded)
            {
                PLogger.Warning(
                    $"[PulletYooAsset] {request.PackageName}: 着色器变体集合加载失败，跳过预热。" +
                    handle.Error);
                handle.Release();
                yield break;
            }

            var collection = handle.AssetObject as ShaderVariantCollection;
            if (collection == null)
            {
                PLogger.Warning(
                    $"[PulletYooAsset] {request.PackageName}: {location} 不是有效的着色器变体集合。");
                handle.Release();
                yield break;
            }

            int variantCount = collection.variantCount;
            while (!collection.WarmUpProgressively(settings.shaderVariantWarmupBatchSize))
            {
                if (operation.CancellationRequested)
                {
                    handle.Release();
                    Cancel(state, operation, finished);
                    yield break;
                }
                float ratio = variantCount == 0
                    ? 1f
                    : (float)collection.warmedUpVariantCount / variantCount;
                SetState(state, operation, EPulletYooAssetPackageStatus.WarmingShaderVariants,
                    0.92f + Mathf.Clamp01(ratio) * 0.04f);
                yield return null;
            }
            PLogger.Info(
                $"[PulletYooAsset] {request.PackageName}: 已预热 {variantCount} 个着色器变体。");
            handle.Release();
        }

        private static bool CheckFallbackResult(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            EOperationStatus status,
            string fallbackError,
            string remoteError,
            string stage)
        {
            if (operation.CancellationRequested)
            {
                Cancel(state, operation, finished);
                return false;
            }
            if (status == EOperationStatus.Succeeded)
                return true;
            FailBuiltinFallback(
                state, operation, finished, remoteError, stage, fallbackError);
            return false;
        }

        private static void FailBuiltinFallback(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            string remoteError,
            string stage,
            string fallbackError)
        {
            Fail(state, operation, finished,
                $"远端资源启动失败：{remoteError}\n"
                + $"内置资源降级也失败（{stage}）：{fallbackError}");
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
            operation.SetSucceeded(null);
            FinishAndNotify(operation, finished, true, true);
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
            return package.PackageValid ? package.GetPackageVersion() : null;
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
            Action<bool> finished,
            string checkedVersion = null)
        {
            state.Status = string.IsNullOrWhiteSpace(state.CurrentVersion)
                ? EPulletYooAssetPackageStatus.Initialized
                : EPulletYooAssetPackageStatus.Ready;
            state.Progress = 1f;
            state.Error = null;
            operation.SetSucceeded(checkedVersion ?? state.CurrentVersion);
            FinishAndNotify(operation, finished, false, true);
        }

        private static void Cancel(
            PulletYooAssetPackageState state,
            PulletYooAssetPackageOperation operation,
            Action<bool> finished)
        {
            state.Error = "操作已取消。";
            state.Status = EPulletYooAssetPackageStatus.Failed;
            operation.SetFailed(state.Error, true);
            FinishAndNotify(operation, finished, false, false);
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
            operation.SetFailed(state.Error);
            PLogger.Error($"[PulletYooAsset] {operation.PackageName}: {state.Error}");
            FinishAndNotify(operation, finished, false, false);
        }

        internal static void FinishAndNotify(
            PulletYooAssetPackageOperation operation,
            Action<bool> finished,
            bool removeState,
            bool reportFinalProgress)
        {
            try
            {
                finished(removeState);
            }
            catch (Exception exception)
            {
                PLogger.Exception(exception,
                    $"[PulletYooAsset] {operation.PackageName} finalization failed.");
            }
            finally
            {
                operation.NotifyCompleted(reportFinalProgress);
            }
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
