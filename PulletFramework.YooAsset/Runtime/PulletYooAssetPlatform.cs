using UnityEngine;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>统一 YooAsset 构建发布与运行时下载使用的平台目录名称。</summary>
    public static class PulletYooAssetPlatform
    {
        public static string Current
        {
            get
            {
#if UNITY_EDITOR
                return FromBuildTarget(UnityEditor.EditorUserBuildSettings.activeBuildTarget);
#else
                return FromRuntimePlatform(Application.platform);
#endif
            }
        }

        public static string FromRuntimePlatform(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "IPhone";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                case RuntimePlatform.WindowsPlayer: return "Windows";
                case RuntimePlatform.OSXPlayer: return "macOS";
                case RuntimePlatform.LinuxPlayer: return "Linux";
                default: return platform.ToString();
            }
        }

#if UNITY_EDITOR
        public static string FromBuildTarget(UnityEditor.BuildTarget target)
        {
            switch (target)
            {
                case UnityEditor.BuildTarget.Android: return "Android";
                case UnityEditor.BuildTarget.iOS: return "IPhone";
                case UnityEditor.BuildTarget.WebGL: return "WebGL";
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64:
                    return "Windows";
                case UnityEditor.BuildTarget.StandaloneOSX: return "macOS";
                case UnityEditor.BuildTarget.StandaloneLinux64: return "Linux";
                default: return target.ToString();
            }
        }
#endif
    }
}
