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
                if (IsDone)
                    value(this);
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        internal bool CancellationRequested { get; private set; }

        internal PulletYooAssetPackageOperation(
            string packageName, EPulletYooAssetOperationType operationType)
        {
            PackageName = packageName;
            OperationType = operationType;
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
            ProgressChanged?.Invoke(this);
        }

        internal void ReportDownloadFileStarted(string fileName, long fileSize)
        {
            CurrentDownloadFile = fileName;
            DownloadFileStarted?.Invoke(this, fileName, fileSize);
        }

        internal void ReportDownloadError(string fileName, string error)
        {
            LastDownloadError = error;
            DownloadError?.Invoke(this, fileName, error);
        }

        internal void Complete(string packageVersion)
        {
            if (IsDone)
                return;
            PackageVersion = packageVersion;
            Progress = 1f;
            Succeeded = true;
            IsDone = true;
            _downloader = null;
            ProgressChanged?.Invoke(this);
            Action<PulletYooAssetPackageOperation> callback = _completed;
            _completed = null;
            callback?.Invoke(this);
        }

        internal void Fail(string error, bool cancelled = false)
        {
            if (IsDone)
                return;
            Error = string.IsNullOrWhiteSpace(error) ? "Unknown YooAsset operation error." : error;
            IsCancelled = cancelled;
            IsDone = true;
            _downloader = null;
            Action<PulletYooAssetPackageOperation> callback = _completed;
            _completed = null;
            callback?.Invoke(this);
        }
    }
}
