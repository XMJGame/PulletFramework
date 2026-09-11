using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>通用 Player 构建入口，供 Workspace 和自动化流程复用。</summary>
    public static class PulletPlayerBuildService
    {
        public static bool Validate(PulletBuildSetting setting, out string error)
        {
            BuildTarget target = setting.GetBuildTarget();
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                error = $"当前 Unity 未安装 {target} 的构建支持模块。";
            else if (EditorUserBuildSettings.activeBuildTarget != target)
                error = $"当前活动平台是 {EditorUserBuildSettings.activeBuildTarget}，请先切换到 {target}。";
            else if (GetEnabledScenes().Length == 0)
                error = "Build Settings 中没有启用的场景。";
            else if (string.IsNullOrWhiteSpace(setting.appVersion))
                error = "Player 版本不能为空。";
            else
            {
                error = null;
                return true;
            }

            return false;
        }

        public static bool SwitchActiveBuildTarget(PulletBuildSetting setting)
        {
            BuildTarget target = setting.GetBuildTarget();
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                EditorUtility.DisplayDialog("无法切换平台", $"未安装 {target} 的构建支持模块。", "确定");
                return false;
            }

            return EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        }

        public static BuildReport Build(PulletBuildSetting setting)
        {
            if (!Validate(setting, out string error))
                throw new InvalidOperationException(error);

            ApplyPlayerSettings(setting);
            string outputPath = GetOutputPath(setting);
            string outputDirectory = IsDirectoryOutput(setting.GetBuildTarget())
                ? outputPath
                : Path.GetDirectoryName(outputPath);
            Directory.CreateDirectory(outputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = outputPath,
                targetGroup = BuildPipeline.GetBuildTargetGroup(setting.GetBuildTarget()),
                target = setting.GetBuildTarget(),
                options = setting.GetBuildTarget() == BuildTarget.Android
                    ? BuildOptions.CompressWithLz4
                    : BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[PulletPlayerBuild] 构建完成：{outputPath}");
                EditorUtility.RevealInFinder(outputPath);
            }
            else
            {
                Debug.LogError($"[PulletPlayerBuild] 构建失败：{report.summary.result}");
            }

            return report;
        }

        public static string GetOutputPath(PulletBuildSetting setting)
        {
            BuildTarget target = setting.GetBuildTarget();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Player",
                target.ToString()));
            string fileName = SanitizeFileName(
                $"{PlayerSettings.productName}-{setting.appVersion}");
            switch (target)
            {
                case BuildTarget.Android:
                    return Path.Combine(root, fileName + ".apk");
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(root, fileName + ".exe");
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(root, fileName + ".app");
                default:
                    return root;
            }
        }

        public static string[] GetEnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }

        private static void ApplyPlayerSettings(PulletBuildSetting setting)
        {
            PlayerSettings.bundleVersion = setting.appVersion;
            if (setting.GetBuildTarget() != BuildTarget.Android)
                return;

            PlayerSettings.Android.bundleVersionCode = Math.Max(1, setting.appVersionCode);
            if (string.IsNullOrWhiteSpace(setting.keystoreName))
                return;

            PlayerSettings.Android.keystoreName = setting.keystoreName;
            PlayerSettings.Android.keystorePass = setting.keystorePass;
            PlayerSettings.Android.keyaliasName = setting.keyaliasName;
            PlayerSettings.Android.keyaliasPass = setting.keyaliasPass;
        }

        private static bool IsDirectoryOutput(BuildTarget target)
        {
            return target == BuildTarget.iOS || target == BuildTarget.WebGL;
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalidCharacter, '_');
            return fileName;
        }
    }
}
