using UnityEngine;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>YooAsset 运行模式。编辑器与真机可分别配置，避免测试配置进入发布包。</summary>
    public enum EPulletYooAssetPlayMode
    {
        EditorSimulate,
        Offline,
        Host,
        Web
    }

    /// <summary>YooAsset 的独立配置。这里的 CDN 只承载业务资源，不是小游戏首包 CDN。</summary>
    [CreateAssetMenu(fileName = "PulletYooAssetSettings", menuName = "Pullet/YooAsset Settings")]
    public sealed class PulletYooAssetSettings : ScriptableObject
    {
        [Header("Package")]
        public string packageName = "DefaultPackage";
        public EPulletYooAssetPlayMode editorPlayMode = EPulletYooAssetPlayMode.EditorSimulate;
        public EPulletYooAssetPlayMode playerPlayMode = EPulletYooAssetPlayMode.Host;
        public EPulletYooAssetPlayMode webPlayMode = EPulletYooAssetPlayMode.Web;

        [Header("Business asset CDN")]
        [Tooltip("可使用 {platform}、{appVersion}、{package} 占位符。目录应直接包含 YooAsset 版本与清单文件。")]
        public string defaultHostServer = "";
        public string fallbackHostServer = "";
        public string appVersion = "v1.0";

        [Header("Download")]
        public bool downloadAllOnStartup = true;
        [Range(1, 16)] public int maximumConcurrency = 4;
        [Range(0, 10)] public int retryCount = 2;
        [Range(5, 180)] public int timeoutSeconds = 30;
        [Range(1, 100)] public int operationTimeSliceMilliseconds = 30;

        public EPulletYooAssetPlayMode GetRuntimePlayMode()
        {
#if UNITY_EDITOR
            return editorPlayMode;
#else
            return Application.platform == RuntimePlatform.WebGLPlayer ? webPlayMode : playerPlayMode;
#endif
        }

        public string ResolveDefaultHostServer() => ResolveHost(defaultHostServer);

        public string ResolveFallbackHostServer()
        {
            string value = string.IsNullOrWhiteSpace(fallbackHostServer)
                ? defaultHostServer
                : fallbackHostServer;
            return ResolveHost(value);
        }

        private string ResolveHost(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            return template.TrimEnd('/')
                .Replace("{platform}", GetPlatformName())
                .Replace("{appVersion}", appVersion.Trim('/'))
                .Replace("{package}", packageName.Trim('/'));
        }

        private static string GetPlatformName()
        {
#if UNITY_EDITOR
            switch (UnityEditor.EditorUserBuildSettings.activeBuildTarget)
            {
                case UnityEditor.BuildTarget.Android: return "Android";
                case UnityEditor.BuildTarget.iOS: return "IPhone";
                case UnityEditor.BuildTarget.WebGL: return "WebGL";
                default: return "PC";
            }
#else
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "IPhone";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                default: return "PC";
            }
#endif
        }
    }
}
