using UnityEngine;
using PulletFramework.YooAssetAdapter;

namespace PulletMiniGame.Platform.WeChat
{
    public static class WeChatSdkRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            MiniGameBootstrap.Register(PulletPlatformIds.WeChat,
                () => new WeChatPlatformAdapter(new WeChatSdkBridge()));
#if UNITY_WEBGL && (WEIXINMINIGAME || UNITY_WECHATMINIGAME)
            // 微信需要使用 USER_DATA_PATH 和 WXAssetBundle，避免退回浏览器缓存语义。
            PulletYooAssetRuntime.WebFileSystemFactory =
                (_, remoteService) => WechatFileSystemCreater.CreateFileSystemParameters(remoteService);
#endif
        }
    }
}
