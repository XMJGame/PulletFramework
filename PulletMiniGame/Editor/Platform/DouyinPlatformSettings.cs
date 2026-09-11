namespace PulletMiniGame.Editor
{
    /// <summary>抖音小游戏构建及开发者工具参数。</summary>
    public sealed class DouyinPlatformSettings : MiniGamePlatformSettings
    {
        public string developerToolPath;
        public bool iosHighPerformancePlus;
        public EMiniGameMenuButtonStyle menuButtonStyle = EMiniGameMenuButtonStyle.Light;
        public bool useLegacyBuildFormat;
    }
}
