using System;
using UnityEngine;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    public enum EPulletYooAssetPackageStatus
    {
        None,
        Initializing,
        Initialized,
        CheckingVersion,
        UpdatingManifest,
        Ready,
        Downloading,
        WarmingShaderVariants,
        FallingBackToBuiltin,
        ClearingCache,
        Unloading,
        Failed
    }

    public enum EPulletYooAssetOperationType
    {
        Initialize,
        Check,
        Update,
        Download,
        Prepare,
        ClearUnusedCache,
        UnloadUnusedAssets,
        Unload
    }

    /// <summary>某个资源包当前的只读运行状态。</summary>
    public sealed class PulletYooAssetPackageState
    {
        public string PackageName { get; internal set; }
        public EPulletYooAssetPackageStatus Status { get; internal set; }
        public string CurrentVersion { get; internal set; }
        public string RemoteVersion { get; internal set; }
        public bool HasUpdate { get; internal set; }
        public float Progress { get; internal set; }
        public int TotalDownloadCount { get; internal set; }
        public long TotalDownloadBytes { get; internal set; }
        public int CurrentDownloadCount { get; internal set; }
        public long CurrentDownloadBytes { get; internal set; }
        public string Error { get; internal set; }
        public bool UsedBuiltinFallback { get; internal set; }
        public string FallbackReason { get; internal set; }

        internal PulletYooAssetPackageState(string packageName)
        {
            PackageName = packageName;
        }
    }

    /// <summary>
    /// 可等待的资源包操作。下载阶段支持暂停、继续和取消，适合直接驱动更新界面。
    /// </summary>
    public sealed class PulletYooAssetPackageOperation : CustomYieldInstruction
    {
        private ResourceDownloaderOperation _downloader;
        private event Action<PulletYooAssetPackageOperation> _completed;
        private bool _completionInvoked;

        public string PackageName { get; }
        public EPulletYooAssetOperationType OperationType { get; }
        public bool IsDone { get; private set; }
        public bool Succeeded { get; private set; }
        public bool IsCancelled { get; private set; }
        public string Error { get; private set; }
        public string PackageVersion { get; internal set; }
        public float Progress { get; internal set; }
        public int TotalDownloadCount { get; internal set; }
        public long TotalDownloadBytes { get; internal set; }
        public int CurrentDownloadCount { get; internal set; }
        public long CurrentDownloadBytes { get; internal set; }
        public string CurrentDownloadFile { get; private set; }
        public string LastDownloadError { get; private set; }
        public bool UsedBuiltinFallback { get; private set; }
        public string FallbackReason { get; private set; }
        public override bool keepWaiting => !IsDone;

        public event Action<PulletYooAssetPackageOperation> ProgressChanged;
        public event Action<PulletYooAssetPackageOperation, string, long> DownloadFileStarted;
        public event Action<PulletYooAssetPackageOperation, string, string> DownloadError;
        public event Action<PulletYooAssetPackageOperation> Completed
        {
            add
            {
                if (value == null)
                    return;
                if (_completionInvoked)
                    InvokeCallbacks(value, callback => callback(this), "late completion");
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        internal bool CancellationRequested { get; private set; }
        internal int Generation { get; }

        internal PulletYooAssetPackageOperation(
            string packageName, EPulletYooAssetOperationType operationType, int generation = 0)
        {
            PackageName = packageName;
            OperationType = operationType;
            Generation = generation;
        }

        public void PauseDownload()
        {
            _downloader?.PauseDownload();
        }

        public void ResumeDownload()
        {
            _downloader?.ResumeDownload();
        }

        public void Cancel()
        {
            if (IsDone)
                return;
            CancellationRequested = true;
            _downloader?.CancelDownload();
        }

        internal void BindDownloader(ResourceDownloaderOperation downloader)
        {
            _downloader = downloader;
        }

        internal void ReportProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
            InvokeCallbacks(ProgressChanged, callback => callback(this), "progress");
        }

        internal void ReportDownloadFileStarted(string fileName, long fileSize)
        {
            CurrentDownloadFile = fileName;
            InvokeCallbacks(DownloadFileStarted,
                callback => callback(this, fileName, fileSize), "download started");
        }

        internal void ReportDownloadError(string fileName, string error)
        {
            LastDownloadError = error;
            InvokeCallbacks(DownloadError,
                callback => callback(this, fileName, error), "download error");
        }

        internal void MarkBuiltinFallback(string reason)
        {
            UsedBuiltinFallback = true;
            FallbackReason = reason;
            _downloader = null;
            TotalDownloadCount = 0;
            TotalDownloadBytes = 0;
            CurrentDownloadCount = 0;
            CurrentDownloadBytes = 0;
            CurrentDownloadFile = null;
            LastDownloadError = null;
        }

        internal void SetSucceeded(string packageVersion)
        {
            if (IsDone)
                return;
            PackageVersion = packageVersion;
            Progress = 1f;
            Succeeded = true;
            IsDone = true;
            _downloader = null;
        }

        internal void SetFailed(string error, bool cancelled = false)
        {
            if (IsDone)
                return;
            Error = string.IsNullOrWhiteSpace(error) ? "Unknown YooAsset operation error." : error;
            IsCancelled = cancelled;
            IsDone = true;
            _downloader = null;
        }

        internal void FailImmediately(string error, bool cancelled = false)
        {
            SetFailed(error, cancelled);
            NotifyCompleted();
        }

        internal void CancelImmediately(string error)
        {
            if (IsDone)
                return;
            CancellationRequested = true;
            _downloader?.CancelDownload();
            SetFailed(error, true);
            NotifyCompleted();
        }

        internal void NotifyCompleted(bool reportFinalProgress = false)
        {
            if (_completionInvoked || !IsDone)
                return;
            _completionInvoked = true;
            if (reportFinalProgress)
                InvokeCallbacks(ProgressChanged, callback => callback(this), "final progress");
            Action<PulletYooAssetPackageOperation> callback = _completed;
            _completed = null;
            InvokeCallbacks(callback, item => item(this), "completion");
            ProgressChanged = null;
            DownloadFileStarted = null;
            DownloadError = null;
        }

        private void InvokeCallbacks<TDelegate>(
            TDelegate callbacks, Action<TDelegate> invoke, string callbackType)
            where TDelegate : Delegate
        {
            if (callbacks == null)
                return;
            Delegate[] invocationList = callbacks.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    invoke((TDelegate)invocationList[i]);
                }
                catch (Exception exception)
                {
                    PLogger.Exception(exception,
                        $"[PulletYooAsset] {PackageName} {callbackType} callback failed.");
                }
            }
        }
    }
}
