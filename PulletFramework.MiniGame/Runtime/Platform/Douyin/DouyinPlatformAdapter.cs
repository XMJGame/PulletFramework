using PulletFramework.MiniGame.Platform;

namespace PulletFramework.MiniGame.Platform.Douyin
{
    public interface IDouyinSdkBridge : IMiniGameSdkBridge { }

    public sealed class DouyinPlatformAdapter : BridgePlatformAdapter
    {
        public DouyinPlatformAdapter(IDouyinSdkBridge bridge) : base(bridge) { }
    }
}
