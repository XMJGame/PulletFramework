using UnityEngine;
using PulletFramework.YooAssetAdapter;
using PulletFramework.Sound;
using PulletFramework.Window;

namespace PulletMiniGame.Platform.Douyin
{
    public static class DouyinSdkRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            MiniGameBootstrap.Register(PulletPlatformIds.Douyin,
                () => new DouyinPlatformAdapter(new DouyinSdkBridge()));
#if UNITY_WEBGL && (PULLET_PLATFORM_DOUYIN || DOUYINMINIGAME)
            UISafeArea.SetProvider(new DouyinSafeAreaProvider());
            PulletSound.SetMusicBackend(new DouyinMusicBackend());
            // 使用 TTAssetBundle 完成平台侧下载、解包和卸载。
            PulletYooAssets.WebFileSystemFactory =
                (_, remoteService) => TiktokFileSystemCreater.CreateFileSystemParameters(remoteService);
#endif
        }
    }
}
