using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>HybridCLR 构建与程序集复制工具。</summary>
    public sealed class PulletHybridClrWorkspaceModule : IPulletWorkspaceModule
    {
        public string Id => "hybridclr";
        public string DisplayName => "HybridCLR";
        public string Description => "生成、编译并复制 HybridCLR 所需程序集。";
        public int Order => 400;

        public void OnEnable() { }
        public void OnDisable() { }

        public void OnGUI()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            EditorGUILayout.HelpBox(
                $"当前目标平台：{target}\n生成 AOT DLL 会触发 HybridCLR 的临时裁剪构建，请只在正式热更新流程中执行。",
                MessageType.Info);

            if (GUILayout.Button("生成 AOT 补充元数据程序集", GUILayout.Height(32f)))
                HybridCLRCommand.GenerateAOTDlls(target);
            if (GUILayout.Button("编译热更新程序集", GUILayout.Height(32f)))
                HybridCLRCommand.CompileDll(target);
            if (GUILayout.Button("生成、编译并复制全部程序集", GUILayout.Height(32f)))
                HybridCLRCommand.BuildAndCopyDlls();

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("仅复制 AOT 程序集"))
                    HybridCLRCommand.CopyAOTAssembliesToAssetPath();
                if (GUILayout.Button("仅复制热更新程序集"))
                    HybridCLRCommand.CopyHotUpdateAssembliesToAssetPath();
            }
        }
    }
}
