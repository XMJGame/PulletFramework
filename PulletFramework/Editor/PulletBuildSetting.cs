using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>Unity Player 构建目标。</summary>
    public enum EBuildTarget
    {
        Android,
        iOS,
        StandaloneWindows,
        StandaloneWindows64,
        StandaloneOSX,
        WebGL
    }

    /// <summary>通用 Player 构建配置，不包含资源系统或小游戏平台参数。</summary>
    [CreateAssetMenu(fileName = "PulletBuildSetting", menuName = "Pullet/Create Pullet Build Settings")]
    public sealed class PulletBuildSetting : ScriptableObject
    {
        public EBuildTarget buildTarget;
        public string appVersion = "1.0.0";
        public int appVersionCode = 1;

        [Header("Android Signing")]
        public string keystoreName = "";
        public string keystorePass = "";
        public string keyaliasName = "";
        public string keyaliasPass = "";

        public BuildTarget GetBuildTarget()
        {
            switch (buildTarget)
            {
                case EBuildTarget.Android: return BuildTarget.Android;
                case EBuildTarget.iOS: return BuildTarget.iOS;
                case EBuildTarget.StandaloneWindows: return BuildTarget.StandaloneWindows;
                case EBuildTarget.StandaloneWindows64: return BuildTarget.StandaloneWindows64;
                case EBuildTarget.StandaloneOSX: return BuildTarget.StandaloneOSX;
                case EBuildTarget.WebGL: return BuildTarget.WebGL;
                default: return BuildTarget.Android;
            }
        }
    }
}
