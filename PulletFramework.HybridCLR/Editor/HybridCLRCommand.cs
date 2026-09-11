using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    public class HybridCLRCommand
    {
        public static string AOTAssembliesPath = "Art/Assembly/AOT";
        public static string HotUpdateAssembliesPath = "Art/Assembly/HotUpdate";

        public static string[] GetHotUpdateAssemblyNames()
        {
            return SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.ToArray();
        }

        public static string[] GetAOTAssemblyNames()
        {
            return SettingsUtil.HybridCLRSettings.patchAOTAssemblies ?? System.Array.Empty<string>();
        }

        /// <summary>只验证 API 和配置可读性，不生成 DLL，也不触发 Player 构建。</summary>
        public static void ValidateIntegration()
        {
            string[] hotUpdateAssemblies = GetHotUpdateAssemblyNames();
            string[] aotAssemblies = GetAOTAssemblyNames();
            Debug.Log($"[PulletHybridCLR] API 检查通过：HotUpdate={hotUpdateAssemblies.Length}, AOT={aotAssemblies.Length}");
        }

        /// <summary>
        /// 生成AOT Dlls
        /// </summary>
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
                ClearDirectory(aotAssembliesDstDir);
            }

            foreach (string dll in GetAOTAssemblyNames())
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
                ClearDirectory(hotfixAssembliesDstDir);
            }
            foreach (string dll in GetHotUpdateAssemblyNames())
            {
                string dllPath = $"{hotfixDllSrcDir}/{dll}.dll";
                string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
                System.IO.File.Copy(dllPath, dllBytesPath, true);
                Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
            }
            AssetDatabase.Refresh();
            AssetDatabase.Refresh();
        }

        private static void ClearDirectory(string directory)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                FileUtil.DeleteFileOrDirectory(entry);
        }
    }
}
