#if UNITY_WEBGL && (PULLET_PLATFORM_DOUYIN || DOUYINMINIGAME)
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;
using TTSDK;
using PulletFramework.YooAssetAdapter;

/// <summary>
/// 抖音小游戏平台实现
/// </summary>
internal class TiktokPlatform : IPulletWebPlatformStrategy
{
    /// <inheritdoc/>
    public UnityWebRequest CreateAssetBundleRequest(PulletWebAssetBundleRequest args)
    {
        UnityWebRequest request = TTAssetBundle.GetAssetBundle(args.Url);
        request.disposeDownloadHandlerOnDispose = true;
        return request;
    }

    /// <inheritdoc/>
    public AssetBundle ExtractAssetBundle(UnityWebRequest request)
    {
        var downloadHandler = (DownloadHandlerTTAssetBundle)request.downloadHandler;
        return downloadHandler.assetBundle;
    }

    /// <inheritdoc/>
    public void UnloadAssetBundle(AssetBundle assetBundle, bool unloadAll)
    {
        assetBundle.TTUnload(unloadAll);
    }
}
#endif
