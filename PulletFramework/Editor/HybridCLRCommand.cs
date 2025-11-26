#if ENABLE_HYBRIDCLR_EDITOR
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace PulletFramework.Editor
{
    public class HybridCLRCommand
    {
        public static string AOTAssembliesPath = "Art/Assembly/AOT";
        public static string HotUpdateAssembliesPath = "Art/Assembly/HotUpdate";

        /// <summary>
        /// 生成AOT Dlls
        /// </summary>
        [MenuItem("Pullets/Build/Generate AOT Dlls", priority = 101)]
        public static void GenerateAOTDlls()
        {
            StripAOTDllCommand.GenerateStripedAOTDlls(EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 生成AOT Dlls
        /// </summary>
        /// <param name="target"></param>
        public static void GenerateAOTDlls(BuildTarget target)
        {
            StripAOTDllCommand.GenerateStripedAOTDlls(target);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 编译热梗Dll
        /// </summary>
        [MenuItem("Pullets/Build/Compile HotUpdate Dlls", priority = 102)]
        public static void CompileDll()
        {
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 编译热梗Dll
        /// </summary>
        /// <param name="target"></param>
        public static void CompileDll(BuildTarget target)
        {
            CompileDllCommand.CompileDll(target);
            AssetDatabase.Refresh();
        }


        [MenuItem("Pullets/Build/Build All Dlls  And CopyTo Art-Assembly", priority = 103)]
        public static void BuildAndCopyDlls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            GenerateAOTDlls(target);
            CompileDll(target);
            CopyAOTHotUpdateDlls(target);
        }

        public static void CopyAOTHotUpdateDlls(BuildTarget target)
        {
            CopyAOTAssembliesToAssetPath();
            CopyHotUpdateAssembliesToAssetPath();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// copy AOTdll
        /// </summary>
        [MenuItem("Pullets/Build/Copy AOT Dlls", priority = 104)]
        public static void CopyAOTAssembliesToAssetPath()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string aotAssembliesDstDir = Application.dataPath + "/" + AOTAssembliesPath;

            if (!Directory.Exists(aotAssembliesDstDir))
            {
                Directory.CreateDirectory(aotAssembliesDstDir);
            }
            else
            {
                //清空
                EditorTools.ClearFolder(aotAssembliesDstDir);
            }

            foreach (var dll in SettingsUtil.AOTAssemblyNames)
            {
                string srcDllPath = $"{aotAssembliesSrcDir}/{dll}.dll";
                if (!System.IO.File.Exists(srcDllPath))
                {
                    Debug.LogError($"ab中添加AOT补充元数据dll:{srcDllPath} 时发生错误,文件不存在。裁剪后的AOT dll在BuildPlayer时才能生成，因此需要你先构建一次游戏App后再打包。");
                    continue;
                }
                string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.bytes";
                System.IO.File.Copy(srcDllPath, dllBytesPath, true);
                Debug.Log($"[CopyAOTAssembliesToStreamingAssets] copy AOT dll {srcDllPath} -> {dllBytesPath}");
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// copy HotUpdate dll
        /// </summary>
        [MenuItem("Pullets/Build/Copy HotUpdate Dlls", priority = 105)]
        public static void CopyHotUpdateAssembliesToAssetPath()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;

            string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotfixAssembliesDstDir = Application.dataPath + "/" + HotUpdateAssembliesPath;

            if (!Directory.Exists(hotfixAssembliesDstDir))
            {
                Directory.CreateDirectory(hotfixAssembliesDstDir);
            }
            else
            {
                //清空
                EditorTools.ClearFolder(hotfixAssembliesDstDir);
            }
            foreach (var dll in SettingsUtil.HotUpdateAssemblyNamesExcludePreserved)
            {
                string dllPath = $"{hotfixDllSrcDir}/{dll}.dll";
                string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
                System.IO.File.Copy(dllPath, dllBytesPath, true);
                Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
            }
            AssetDatabase.Refresh();
            return;

            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
            {
                string dllPath = $"{hotfixDllSrcDir}/{dll}";
                string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
                System.IO.File.Copy(dllPath, dllBytesPath, true);
                Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
            }

            AssetDatabase.Refresh();
        }
    }
}
#endif