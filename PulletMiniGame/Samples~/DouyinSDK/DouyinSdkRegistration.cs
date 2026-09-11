using UnityEngine;
using PulletFramework.YooAssetAdapter;

namespace PulletMiniGame.Platform.Douyin
{
    public static class DouyinSdkRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            MiniGameBootstrap.Register(PulletPlatformIds.Douyin,
                () => new DouyinPlatformAdapter(new DouyinSdkBridge()));
#if UNITY_WEBGL && DOUYINMINIGAME
            // 使用 TTAssetBundle 完成平台侧下载、解包和卸载。
            PulletYooAssetRuntime.WebFileSystemFactory =
                (_, remoteService) => TiktokFileSystemCreater.CreateFileSystemParameters(remoteService);
#endif
        }
    }
}
