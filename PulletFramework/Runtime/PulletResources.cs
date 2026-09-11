using System;
using System.Collections;
using UnityEngine;

namespace PulletFramework.Resource
{
    public enum EResourcePackageStatus
    {
        None,
        Initializing,
        Succeeded,
        Failed
    }

    public readonly struct ResourceInstantiateOptions
    {
        public bool IsActive { get; }
        public Transform Parent { get; }
        public bool InWorldSpace { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public bool SetPositionAndRotation { get; }

        public ResourceInstantiateOptions(bool isActive, Transform parent = null, bool inWorldSpace = false)
        {
            IsActive = isActive;
            Parent = parent;
            InWorldSpace = inWorldSpace;
            Position = Vector3.zero;
            Rotation = Quaternion.identity;
            SetPositionAndRotation = false;
        }

        public ResourceInstantiateOptions(bool isActive, Transform parent, Vector3 position, Quaternion rotation)
        {
            IsActive = isActive;
            Parent = parent;
            InWorldSpace = false;
            Position = position;
            Rotation = rotation;
            SetPositionAndRotation = true;
        }
    }

    public interface IResourceAssetHandle : IEnumerator
    {
        bool IsValid { get; }
        bool IsDone { get; }
        bool IsSucceeded { get; }
        string Error { get; }
        UnityEngine.Object AssetObject { get; }
        event Action<IResourceAssetHandle> Completed;
        void WaitForCompletion();
        GameObject InstantiateSync(ResourceInstantiateOptions options);
        IResourceInstanceHandle InstantiateAsync(ResourceInstantiateOptions options);
        void Release();
    }

    public interface IResourceInstanceHandle
    {
        bool IsDone { get; }
        bool IsSucceeded { get; }
        string Error { get; }
        GameObject Result { get; }
        void WaitForCompletion();
        void Cancel();
    }

    public interface IResourcePackage
    {
        string Name { get; }
        EResourcePackageStatus Status { get; }
        string Error { get; }
        bool IsLocationValid(string location);
        IResourceAssetHandle LoadAssetAsync<TObject>(string location) where TObject : UnityEngine.Object;
    }

    public interface IResourceAdapter
    {
        string Name { get; }
        string DefaultPackageName { get; }
        bool TryGetPackage(string packageName, out IResourcePackage package);
        IResourcePackage GetPackage(string packageName);
    }

    public static class PulletResources
    {
        private static IResourceAdapter _adapter;

        public static bool IsConfigured => _adapter != null;
        public static IResourceAdapter Adapter => _adapter ?? throw new InvalidOperationException(
            "No resource adapter is installed. Call PulletResources.Install() during bootstrap.");

        public static void Install(IResourceAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public static void Uninstall()
        {
            _adapter = null;
        }

        public static bool TryGetPackage(string packageName, out IResourcePackage package)
        {
            packageName = string.IsNullOrEmpty(packageName) ? Adapter.DefaultPackageName : packageName;
            return Adapter.TryGetPackage(packageName, out package);
        }

        public static IResourcePackage GetPackage(string packageName = null)
        {
            packageName = string.IsNullOrEmpty(packageName) ? Adapter.DefaultPackageName : packageName;
            return Adapter.GetPackage(packageName);
        }

        public static IResourceAssetHandle LoadAssetAsync<TObject>(string location, string packageName = null)
            where TObject : UnityEngine.Object
        {
            return GetPackage(packageName).LoadAssetAsync<TObject>(location);
        }
    }
}
