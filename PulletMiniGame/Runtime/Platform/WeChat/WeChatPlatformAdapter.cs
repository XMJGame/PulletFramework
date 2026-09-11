using PulletMiniGame.Platform;

namespace PulletMiniGame.Platform.WeChat
{
    public interface IWeChatSdkBridge : IMiniGameSdkBridge { }

    public sealed class WeChatPlatformAdapter : BridgePlatformAdapter
    {
        public WeChatPlatformAdapter(IWeChatSdkBridge bridge) : base(bridge) { }
    }
}
