using PulletFramework.Editor;
using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    /// <summary>小游戏模块安装后自动加入 Pullet Workspace。</summary>
    public sealed class PulletMiniGameWorkspaceModule : IPulletWorkspaceModule
    {
        private Vector2 _scroll;

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
            }

            GUILayout.Space(8f);
            PulletMiniGameBuildWindow.Draw(ref _scroll, false);
        }
    }
}
