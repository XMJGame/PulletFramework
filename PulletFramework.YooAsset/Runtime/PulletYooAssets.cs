using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>YooAsset 多资源包对外入口。业务代码只依赖该门面和操作结果。</summary>
    public static class PulletYooAssets
    {
        public delegate FileSystemParameters WebFileSystemFactoryDelegate(
            string packageName, IRemoteService remoteService);

        private sealed class CoroutineRunner : MonoBehaviour { }

        private static readonly Dictionary<string, PulletYooAssetPackageState> States =
            new Dictionary<string, PulletYooAssetPackageState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PulletYooAssetPackageOperation> ActiveOperations =
            new Dictionary<string, PulletYooAssetPackageOperation>(StringComparer.Ordinal);
        private static CoroutineRunner _runner;

        /// <summary>默认启动包名称。配置不可用时返回 DefaultPackage。</summary>
        public static string DefaultPackageName => PulletYooAssetSettingsData.DefaultPackageName;

        /// <summary>项目是否存在有效的 YooAsset 运行配置。</summary>
        public static bool IsConfigured => PulletYooAssetSettingsData.IsAvailable;

        /// <summary>由微信、抖音等平台模块注入适合平台缓存语义的文件系统。</summary>
        public static WebFileSystemFactoryDelegate WebFileSystemFactory
        {
            get => PulletYooAssetPackageInitializer.WebFileSystemFactory;
            set => PulletYooAssetPackageInitializer.WebFileSystemFactory = value;
        }

        /// <summary>按照项目配置完成默认包初始化、版本更新和可选下载。</summary>
        public static PulletYooAssetPackageOperation PrepareDefaultPackageAsync()
        {
            return PreparePackageAsync(
                DefaultPackageName, PulletYooAssetSettingsData.DownloadAllOnStartup);
        }

        public static PulletYooAssetPackageOperation InitializePackageAsync(string packageName)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.Initialize));
        }

        /// <summary>获取远端版本并与当前已加载清单比较，不改变活动清单。</summary>
        public static PulletYooAssetPackageOperation CheckPackageAsync(string packageName)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.Check));
        }

        /// <summary>更新指定包的活动清单。targetVersion 为空时使用服务器最新版本。</summary>
        public static PulletYooAssetPackageOperation UpdatePackageAsync(
            string packageName, string targetVersion = null)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.Update, targetVersion));
        }

        /// <summary>更新清单并下载全包或指定标签，完成后清理旧缓存。</summary>
        public static PulletYooAssetPackageOperation DownloadPackageAsync(
            string packageName, params string[] tags)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.Download,
                download: true, clearUnusedCache: true, tags: tags));
        }

        /// <summary>完成初始化、版本检查、清单更新和可选下载。</summary>
        public static PulletYooAssetPackageOperation PreparePackageAsync(
            string packageName, bool downloadAll = true, params string[] tags)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.Prepare,
                download: downloadAll, clearUnusedCache: downloadAll, tags: tags));
        }

        /// <summary>销毁并从 YooAssets 移除指定资源包。</summary>
        public static PulletYooAssetPackageOperation UnloadPackageAsync(string packageName)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.Unload));
        }

        /// <summary>卸载指定包内引用计数为零的资源，不销毁 Package。</summary>
        public static PulletYooAssetPackageOperation UnloadUnusedAssetsAsync(string packageName)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.UnloadUnusedAssets));
        }

        /// <summary>清理指定包当前清单不再使用的缓存文件。</summary>
        public static PulletYooAssetPackageOperation ClearUnusedCacheAsync(string packageName)
        {
            return Start(new PulletYooAssetPipelineRequest(
                packageName, EPulletYooAssetOperationType.ClearUnusedCache));
        }

        public static bool IsPackageReady(string packageName)
        {
            return TryGetPackageState(packageName, out PulletYooAssetPackageState state)
                && state.Status == EPulletYooAssetPackageStatus.Ready;
        }

        public static bool TryGetPackageState(
            string packageName, out PulletYooAssetPackageState state)
        {
            return States.TryGetValue(NormalizePackageName(packageName), out state);
        }

        public static PulletYooAssetPackageState GetPackageState(string packageName)
        {
            string normalized = NormalizePackageName(packageName);
            if (!States.TryGetValue(normalized, out PulletYooAssetPackageState state))
            {
                state = new PulletYooAssetPackageState(normalized);
                States.Add(normalized, state);
            }
            return state;
        }

        /// <summary>尝试卸载一个已经没有引用的资源。</summary>
        public static void TryUnloadUnusedAsset(
            string packageName, string location, int maxLoopCount = 10)
        {
            EnsureReady(packageName);
            GetPackage(packageName).TryUnloadUnusedAsset(location, maxLoopCount);
        }

        /// <summary>获取已初始化的 YooAsset 原生资源包，供业务直接使用官方加载 API。</summary>
        public static ResourcePackage GetPackage(string packageName = null)
        {
            packageName = string.IsNullOrWhiteSpace(packageName)
                ? DefaultPackageName
                : packageName;
            string normalized = NormalizePackageName(packageName);
            if (!YooAssets.IsInitialized
                || !YooAssets.TryGetPackage(normalized, out ResourcePackage package))
                throw new InvalidOperationException($"资源包尚未初始化：{normalized}");
            return package;
        }

        public static void Reset(bool destroyYooAssets = false)
        {
            foreach (PulletYooAssetPackageOperation operation in ActiveOperations.Values)
                operation.Cancel();
            ActiveOperations.Clear();
            States.Clear();

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner.gameObject);
                _runner = null;
            }
            if (destroyYooAssets && YooAssets.IsInitialized)
                YooAssets.Destroy();
        }

        private static PulletYooAssetPackageOperation Start(PulletYooAssetPipelineRequest request)
        {
            string packageName = NormalizePackageName(request.PackageName);
            request = request.WithPackageName(packageName);
            var operation = new PulletYooAssetPackageOperation(packageName, request.OperationType);
            if (ActiveOperations.ContainsKey(packageName))
            {
                operation.Fail($"资源包 {packageName} 已有操作正在执行，请等待完成后重试。");
                return operation;
            }

            PulletYooAssetSettings settings = PulletYooAssetSettingsData.Setting;
            if (settings == null)
            {
                operation.Fail("未找到 PulletYooAssetSettings 配置。");
                return operation;
            }

            PulletYooAssetPackageState state = GetPackageState(packageName);
            ActiveOperations.Add(packageName, operation);
            EnsureRunner().StartCoroutine(PulletYooAssetPackagePipeline.Run(
                request, settings, state, operation,
                removeState => Finish(operation, removeState)));
            return operation;
        }

        private static void Finish(PulletYooAssetPackageOperation operation, bool removeState)
        {
            ActiveOperations.Remove(operation.PackageName);
            if (removeState)
                States.Remove(operation.PackageName);
        }

        private static void EnsureReady(string packageName)
        {
            if (!IsPackageReady(packageName))
                throw new InvalidOperationException(
                    $"资源包尚未准备完成：{NormalizePackageName(packageName)}。" +
                    "请先等待 PreparePackageAsync 或 UpdatePackageAsync 成功。");
        }

        private static CoroutineRunner EnsureRunner()
        {
            if (_runner != null)
                return _runner;
            var gameObject = new GameObject("[PulletYooAssets]")
            {
                hideFlags = HideFlags.HideInHierarchy
            };
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            _runner = gameObject.AddComponent<CoroutineRunner>();
            return _runner;
        }

        internal static string NormalizePackageName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("资源包名称不能为空。", nameof(packageName));
            return packageName.Trim();
        }
    }
}
