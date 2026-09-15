namespace PulletMiniGame.Editor
{
    /// <summary>抖音小游戏构建及开发者工具参数。</summary>
    public sealed class DouyinPlatformSettings : MiniGamePlatformSettings
    {
        // 加载图使用基类的 startupImage；可按项目决定是否展示 TTSDK 启动进度条。
        public bool showEngineLoadingBar;
        public UnityEngine.Color loadingBarBackgroundColor = new UnityEngine.Color(0.20f, 0.35f, 0.40f);
        public string developerToolPath;
        public bool iosHighPerformancePlus;
        public EMiniGameMenuButtonStyle menuButtonStyle = EMiniGameMenuButtonStyle.Light;
        public bool useLegacyBuildFormat;
    }
}
