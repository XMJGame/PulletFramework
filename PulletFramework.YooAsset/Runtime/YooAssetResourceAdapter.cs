using System;
using System.Collections.Generic;
using PulletFramework.Resource;
using UnityEngine;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>让 PulletFramework 的基础模块通过 YooAsset 加载单个资源。</summary>
    public sealed class YooAssetResourceAdapter : IResourceAdapter
    {
        public string Name => "YooAsset";
        public string DefaultPackageName { get; }

        public YooAssetResourceAdapter(string defaultPackageName = "DefaultPackage")
        {
            if (string.IsNullOrWhiteSpace(defaultPackageName))
                throw new ArgumentException(
                    "Default package name is required.", nameof(defaultPackageName));
            DefaultPackageName = defaultPackageName;
        }

        public static YooAssetResourceAdapter Install(
            string defaultPackageName = "DefaultPackage")
        {
            if (!YooAssets.IsInitialized)
                throw new InvalidOperationException(
                    "Initialize YooAsset before installing its PulletFramework adapter.");

            if (PulletResources.IsConfigured)
            {
                if (PulletResources.Adapter is YooAssetResourceAdapter installed
                    && string.Equals(installed.DefaultPackageName, defaultPackageName,
                        StringComparison.Ordinal))
                    return installed;
                throw new InvalidOperationException(
                    $"Resource adapter '{PulletResources.Adapter.Name}' is already installed. " +
                    "Call PulletResources.Uninstall() before installing the YooAsset adapter.");
            }

            var adapter = new YooAssetResourceAdapter(defaultPackageName);
            PulletResources.Install(adapter);
            return adapter;
        }

        public bool TryGetPackage(string packageName, out IResourcePackage package)
        {
            if (YooAssets.TryGetPackage(packageName, out ResourcePackage yooPackage))
            {
                package = new YooAssetResourcePackage(yooPackage);
                return true;
            }

            package = null;
            return false;
        }

        public IResourcePackage GetPackage(string packageName)
        {
            return new YooAssetResourcePackage(YooAssets.GetPackage(packageName));
        }
    }

    internal sealed class YooAssetResourcePackage : IResourcePackage
    {
        private readonly ResourcePackage _package;

        public string Name => _package.PackageName;
        public EResourcePackageStatus Status => _package.InitializeStatus == EOperationStatus.Succeeded
            && !_package.PackageValid
                ? EResourcePackageStatus.None
                : ConvertStatus(_package.InitializeStatus);
        public string Error
        {
            get
            {
                if (_package.InitializeStatus == EOperationStatus.Succeeded
                    && !_package.PackageValid)
                    return $"YooAsset package has no active manifest: {_package.PackageName}";
                return Status == EResourcePackageStatus.Failed
                    ? $"YooAsset package initialization failed: {_package.PackageName}"
                    : null;
            }
        }

        public YooAssetResourcePackage(ResourcePackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public bool IsLocationValid(string location)
        {
            return _package.IsLocationValid(location);
        }

        public IResourceAssetHandle LoadAssetAsync<TObject>(string location)
            where TObject : UnityEngine.Object
        {
            return new YooAssetResourceHandle(_package.LoadAssetAsync<TObject>(location));
        }

        private static EResourcePackageStatus ConvertStatus(EOperationStatus status)
        {
            switch (status)
            {
                case EOperationStatus.Processing:
                    return EResourcePackageStatus.Initializing;
                case EOperationStatus.Succeeded:
                    return EResourcePackageStatus.Succeeded;
                case EOperationStatus.Failed:
                    return EResourcePackageStatus.Failed;
                default:
                    return EResourcePackageStatus.None;
            }
        }
    }

    internal sealed class YooAssetResourceHandle : IResourceAssetHandle
    {
        private AssetHandle _handle;
        private event Action<IResourceAssetHandle> _completed;

        public bool IsValid => _handle != null && _handle.IsValid;
        public bool IsDone => _handle == null || _handle.IsDone;
        public bool IsSucceeded => _handle != null
            && _handle.Status == EOperationStatus.Succeeded;
        public string Error => _handle?.Error;
        public UnityEngine.Object AssetObject => _handle?.AssetObject;
        public object Current => null;

        public event Action<IResourceAssetHandle> Completed
        {
            add
            {
                if (value == null)
                    return;
                if (IsDone)
                    InvokeCompleted(value, "late completion");
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        public YooAssetResourceHandle(AssetHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            _handle.Completed += OnCompleted;
        }

        public bool MoveNext() => !IsDone;
        public void Reset() { }

        public void WaitForCompletion()
        {
            _handle?.WaitForAsyncComplete();
        }

        public GameObject InstantiateSync(ResourceInstantiateOptions options)
        {
            EnsureValid();
            return _handle.InstantiateSync(ConvertOptions(options));
        }

        public IResourceInstanceHandle InstantiateAsync(ResourceInstantiateOptions options)
        {
            EnsureValid();
            return new YooAssetInstanceHandle(
                _handle.InstantiateAsync(ConvertOptions(options)));
        }

        public void Release()
        {
            if (_handle == null)
                return;
            if (_handle.IsValid)
            {
                _handle.Completed -= OnCompleted;
                _handle.Release();
            }
            _handle = null;
            _completed = null;
        }

        private void OnCompleted(AssetHandle handle)
        {
            Action<IResourceAssetHandle> callback = _completed;
            _completed = null;
            InvokeCompleted(callback, "completion");
        }

        private void InvokeCompleted(
            Action<IResourceAssetHandle> callbacks, string callbackType)
        {
            if (callbacks == null)
                return;
            Delegate[] invocationList = callbacks.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<IResourceAssetHandle>)invocationList[i])(this);
                }
                catch (Exception exception)
                {
                    PLogger.Exception(exception,
                        $"[PulletYooAsset] Resource handle {callbackType} callback failed.");
                }
            }
        }

        private void EnsureValid()
        {
            if (!IsValid)
                throw new InvalidOperationException(
                    "YooAsset handle is invalid or has been released.");
        }

        private static InstantiateOptions ConvertOptions(
            ResourceInstantiateOptions options)
        {
            if (options.SetPositionAndRotation)
            {
                return new InstantiateOptions(
                    options.IsActive, options.Parent, options.Position, options.Rotation);
            }
            return new InstantiateOptions(
                options.IsActive, options.Parent, options.InWorldSpace);
        }
    }

    internal sealed class YooAssetInstanceHandle : IResourceInstanceHandle
    {
        private readonly InstantiateOperation _operation;

        public bool IsDone => _operation.IsDone;
        public bool IsSucceeded => _operation.Status == EOperationStatus.Succeeded;
        public string Error => _operation.Error;
        public GameObject Result => _operation.Result;

        public YooAssetInstanceHandle(InstantiateOperation operation)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        }

        public void WaitForCompletion()
        {
            _operation.WaitForCompletion();
        }

        public void Cancel()
        {
            _operation.Cancel();
        }
    }

    public sealed class YooAssetRemoteService : IRemoteService
    {
        private readonly string[] _hostServers;

        public YooAssetRemoteService(params string[] hostServers)
        {
            if (hostServers == null || hostServers.Length == 0)
                throw new ArgumentException(
                    "At least one host server is required.", nameof(hostServers));
            _hostServers = hostServers;
        }

        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            if (_hostServers.Length == 2
                && string.Equals(
                    _hostServers[0].TrimEnd('/'),
                    _hostServers[1].TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new[] { $"{_hostServers[0].TrimEnd('/')}/{fileName}" };
            }

            var urls = new string[_hostServers.Length];
            for (int index = 0; index < _hostServers.Length; index++)
                urls[index] = $"{_hostServers[index].TrimEnd('/')}/{fileName}";
            return urls;
        }
    }
}
