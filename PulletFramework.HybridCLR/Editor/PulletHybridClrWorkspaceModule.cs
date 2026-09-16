using System;
using HybridCLR.Editor.Installer;
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
            bool installed = HybridCLRCommand.IsInstalled;
            int hotUpdateCount = HybridCLRCommand.GetHotUpdateAssemblyNames().Length;
            int aotCount = HybridCLRCommand.GetAOTAssemblyNames().Length;

            EditorGUILayout.HelpBox(
                $"当前目标平台：{target}\n" +
                $"Installer：{(installed ? "已安装" : "未安装")}  " +
                $"热更新程序集：{hotUpdateCount}  AOT 补充元数据：{aotCount}",
                installed && hotUpdateCount > 0 ? MessageType.Info : MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开官方 Installer"))
                    OpenInstaller();
                if (GUILayout.Button("打开 HybridCLR Settings"))
                    SettingsService.OpenProjectSettings("Project/HybridCLR Settings");
            }

            if (!installed)
                EditorGUILayout.HelpBox("请先完成官方 Installer，生成操作当前不可用。", MessageType.Warning);
            else if (hotUpdateCount == 0)
                EditorGUILayout.HelpBox("请先配置至少一个热更新程序集。", MessageType.Warning);

            GUILayout.Space(8f);

            DrawEntrySettings();

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!installed))
            {
                if (GUILayout.Button("生成 AOT 补充元数据程序集", GUILayout.Height(32f)))
                    Run("生成 AOT 补充元数据程序集", () => HybridCLRCommand.GenerateAOTDlls(target));
            }
            using (new EditorGUI.DisabledScope(!installed || hotUpdateCount == 0))
            {
                if (GUILayout.Button("编译热更新程序集", GUILayout.Height(32f)))
                    Run("编译热更新程序集", () => HybridCLRCommand.CompileDll(target));
                if (GUILayout.Button("官方 Generate/All 并复制程序集", GUILayout.Height(32f)))
                    Run("生成并复制全部程序集", HybridCLRCommand.BuildAndCopyDlls);
                if (GUILayout.Button("根据已复制 DLL 重新生成清单", GUILayout.Height(28f)))
                    Run("生成热更新清单",
                        () => HybridCLRCommand.GenerateManifest(null));
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("仅复制 AOT 程序集"))
                    Run("复制 AOT 补充元数据程序集",
                        () => HybridCLRCommand.CopyAOTAssembliesToAssetPath(target));
                if (GUILayout.Button("仅复制热更新程序集"))
                    Run("复制热更新程序集",
                        () => HybridCLRCommand.CopyHotUpdateAssembliesToAssetPath(target));
            }

            EditorGUILayout.HelpBox(
                $"输出目录：\nAssets/{HybridCLRCommand.AOTAssembliesPath}\n" +
                $"Assets/{HybridCLRCommand.HotUpdateAssembliesPath}\n" +
                $"{HybridCLRCommand.ManifestAssetPath}\n" +
                "DLL 与清单仍需由 YooAsset 或业务资源系统打包；运行时加载器不绑定具体资源系统。",
                MessageType.None);
        }

        private static void OpenInstaller()
        {
            InstallerWindow window = EditorWindow.GetWindow<InstallerWindow>(
                "HybridCLR Installer", true);
            window.minSize = new Vector2(800f, 500f);
        }

        private static void DrawEntrySettings()
        {
            PulletHybridCLRSettings settings = PulletHybridCLRSettings.instance;
            EditorGUILayout.LabelField("热更新入口", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string assemblyName = EditorGUILayout.TextField(
                "入口程序集", settings.EntryAssembly);
            string typeName = EditorGUILayout.TextField("入口类型", settings.EntryType);
            string methodName = EditorGUILayout.TextField("入口方法", settings.EntryMethod);
            if (EditorGUI.EndChangeCheck())
                settings.SetEntry(assemblyName, typeName, methodName);

            if (string.IsNullOrWhiteSpace(settings.EntryAssembly)
                || string.IsNullOrWhiteSpace(settings.EntryType)
                || string.IsNullOrWhiteSpace(settings.EntryMethod))
            {
                EditorGUILayout.HelpBox(
                    "生成清单前必须配置入口程序集、完整类型名和无参数静态方法。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "入口配置会写入热更新清单，运行时以下载到的清单为准。",
                    MessageType.None);
            }
        }

        private static void Run(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                PLogger.EditorException(exception, $"HybridCLR 操作失败：{operation}");
                EditorUtility.DisplayDialog("HybridCLR 操作失败", exception.Message, "确定");
            }
        }
    }
}
