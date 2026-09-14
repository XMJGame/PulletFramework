using UnityEngine;

namespace PulletMiniGame.Editor
{
    /// <summary>小游戏平台通用的构建参数。</summary>
    public abstract class MiniGamePlatformSettings : ScriptableObject
    {
        public string appId;
        public string gameName;
        public string outputPath;
        public EMiniGameOrientation orientation = EMiniGameOrientation.Portrait;
        public Texture2D startupImage;
        public bool showDefaultUnityLoadingLogo;
        public EFirstPackageResourceMode firstPackageResourceMode = EFirstPackageResourceMode.Package;
        public string cdnUrl;
        [Min(64)] public int initialMemoryMb = 256;
    }
}
