namespace PulletFramework.MiniGame.Editor
{
    /// <summary>微信小游戏构建及开放数据域参数。</summary>
    public sealed class WeChatPlatformSettings : MiniGamePlatformSettings
    {
        public string projectConfigPath;
        public bool iosHighPerformancePlus;
        public bool enableNativeLeaderboard = true;
        public string nativeLeaderboardKey = "user_rank";
    }
}
