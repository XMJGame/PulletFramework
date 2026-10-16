using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PulletNet.Validation.Editor
{
    public static class BuildNetClientPlayerAcceptance
    {
        public static void Build()
        {
            var output = Environment.GetEnvironmentVariable("PULLETNET_PLAYER_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Player output is required.");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Low);
            PlayerSettings.runInBackground = true;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Standalone acceptance driver").AddComponent<NetClientPlayerAcceptance>();
            const string scenePath = "Assets/PlayerAcceptance.unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Scene save failed.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            { scenes = new[] { scenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = output, options = BuildOptions.Development });
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-result.json"),
                JsonUtility.ToJson(new Receipt { result = report.summary.result.ToString(), errors = report.summary.totalErrors,
                    warnings = report.summary.totalWarnings, unity = Application.unityVersion, backend = "Mono", architecture = "x64", stripping = "Low" }));
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Player build failed.");
        }
        [Serializable] private sealed class Receipt { public string result, unity, backend, architecture, stripping; public int errors, warnings; }
    }
}
