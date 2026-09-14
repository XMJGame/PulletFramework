using System;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>平台创建 AssetBundle 请求时需要的公开参数。</summary>
    public readonly struct PulletWebAssetBundleRequest
    {
        public string Url { get; }
        public bool DisableUnityWebCache { get; }
        public string FileHash { get; }
        public uint UnityCrc { get; }

        internal PulletWebAssetBundleRequest(WebAssetBundleRequestArgs args)
        {
            Url = args.Url;
            DisableUnityWebCache = args.DisableUnityWebCache;
            FileHash = args.FileHash;
            UnityCrc = args.UnityCrc;
        }
    }

    /// <summary>小游戏 SDK 对 AssetBundle 下载、提取和卸载行为的适配接口。</summary>
    public interface IPulletWebPlatformStrategy
    {
        UnityWebRequest CreateAssetBundleRequest(PulletWebAssetBundleRequest request);
        AssetBundle ExtractAssetBundle(UnityWebRequest request);
        void UnloadAssetBundle(AssetBundle assetBundle, bool unloadAll);
    }

    /// <summary>创建使用小游戏平台持久缓存语义的 YooAsset Web 文件系统。</summary>
    public static class PulletWebNetworkFileSystem
    {
        public static FileSystemParameters Create(
            IRemoteService remoteService,
            IPulletWebPlatformStrategy platformStrategy,
            IBundleDecryptor assetBundleDecryptor = null,
            IBundleDecryptor rawBundleDecryptor = null)
        {
            if (remoteService == null)
                throw new ArgumentNullException(nameof(remoteService));
            if (platformStrategy == null)
                throw new ArgumentNullException(nameof(platformStrategy));

            FileSystemParameters parameters =
                FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(
                    remoteService, true);
            parameters.AddParameter(
                EFileSystemParameter.WebPlatformStrategy,
                new WebPlatformStrategyBridge(platformStrategy));
            if (assetBundleDecryptor != null)
                parameters.AddParameter(
                    EFileSystemParameter.AssetBundleDecryptor, assetBundleDecryptor);
            if (rawBundleDecryptor != null)
                parameters.AddParameter(
                    EFileSystemParameter.RawBundleDecryptor, rawBundleDecryptor);
            return parameters;
        }

        private sealed class WebPlatformStrategyBridge : IWebPlatformStrategy
        {
            private readonly IPulletWebPlatformStrategy _strategy;

            internal WebPlatformStrategyBridge(IPulletWebPlatformStrategy strategy)
            {
                _strategy = strategy;
            }

            public UnityWebRequest CreateAssetBundleRequest(WebAssetBundleRequestArgs args)
            {
                return _strategy.CreateAssetBundleRequest(
                    new PulletWebAssetBundleRequest(args));
            }

            public AssetBundle ExtractAssetBundle(UnityWebRequest request)
            {
                return _strategy.ExtractAssetBundle(request);
            }

            public void UnloadAssetBundle(AssetBundle assetBundle, bool unloadAll)
            {
                _strategy.UnloadAssetBundle(assetBundle, unloadAll);
            }
        }
    }
}
