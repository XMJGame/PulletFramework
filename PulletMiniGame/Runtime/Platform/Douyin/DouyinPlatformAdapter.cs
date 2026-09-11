using PulletMiniGame.Platform;

namespace PulletMiniGame.Platform.Douyin
{
    public interface IDouyinSdkBridge : IMiniGameSdkBridge { }

    public sealed class DouyinPlatformAdapter : BridgePlatformAdapter
    {
        public DouyinPlatformAdapter(IDouyinSdkBridge bridge) : base(bridge) { }
    }
}
