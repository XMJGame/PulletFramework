using PulletFramework.Editor;
using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace PulletMiniGame.Editor
{
    /// <summary>小游戏模块安装后自动加入 Pullet Workspace。</summary>
    public sealed class PulletMiniGameWorkspaceModule : IPulletWorkspaceModule
    {
        private Vector2 _legacyScroll;
        private bool _showCompatibility;

        public string Id => "minigame";
        public string DisplayName => "小游戏发布";
        public string Description => "切换微信与抖音平台，维护 SDK 参数、平台宏和导出流程。";
        public int Order => 300;

        public void OnEnable() { }
        public void OnDisable() { }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("项目工具", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("初始化标准目录", GUILayout.Height(28f)))
                    PulletMiniGameProjectScaffolder.InitializeProject();
                if (GUILayout.Button("配置腾讯云 COS 下载跨域", GUILayout.Height(28f)))
                    ConfigureCosDownloadCors();
            }

            GUILayout.Space(8f);
            PulletMiniGameBuildWindow.Draw(ref _legacyScroll, false);

            GUILayout.Space(8f);
            _showCompatibility = EditorGUILayout.Foldout(_showCompatibility, "兼容工具", true);
            if (_showCompatibility && GUILayout.Button("打开旧版小游戏构建窗口"))
                PulletMiniGameBuildWindow.Open();
        }

        private static void ConfigureCosDownloadCors()
        {
            try
            {
                bool changed = TencentCOS.EnsureMiniGameDownloadCors();
                EditorUtility.DisplayDialog("COS 跨域配置",
                    changed ? "小游戏资源下载规则已添加，并保留了桶内其他规则。" : "所需规则已经存在，无需修改。",
                    "确定");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("COS 跨域配置失败", exception.Message, "确定");
            }
        }
    }
}
