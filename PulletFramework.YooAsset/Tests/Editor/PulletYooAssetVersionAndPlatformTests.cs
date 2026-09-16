using NUnit.Framework;
using PulletFramework.Editor;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.YooAssetAdapter.Tests
{
    public sealed class PulletYooAssetVersionAndPlatformTests
    {
        [Test]
        public void NewSettingsUseRecognizableShaderVariantDirectory()
        {
            var settings = ScriptableObject.CreateInstance<PulletYooAssetSettings>();
            try
            {
                Assert.That(settings.shaderVariantOutputDirectory,
                    Is.EqualTo("Assets/PulletGenerate/ShaderVariants"));
                Assert.That(PulletYooAssetShaderVariantCollector.GetAssetPath(
                        settings, "DefaultPackage"),
                    Is.EqualTo("Assets/PulletGenerate/ShaderVariants/" +
                        "PulletShaderVariants_DefaultPackage.shadervariants"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(BuildTarget.WebGL, RuntimePlatform.WebGLPlayer, "WebGL")]
        [TestCase(BuildTarget.Android, RuntimePlatform.Android, "Android")]
        [TestCase(BuildTarget.iOS, RuntimePlatform.IPhonePlayer, "IPhone")]
        [TestCase(BuildTarget.StandaloneWindows64, RuntimePlatform.WindowsPlayer, "Windows")]
        [TestCase(BuildTarget.StandaloneOSX, RuntimePlatform.OSXPlayer, "macOS")]
        [TestCase(BuildTarget.StandaloneLinux64, RuntimePlatform.LinuxPlayer, "Linux")]
        public void BuildAndRuntimePlatformDirectoriesMatch(
            BuildTarget buildTarget, RuntimePlatform runtimePlatform, string expected)
        {
            Assert.That(PulletYooAssetPlatform.FromBuildTarget(buildTarget), Is.EqualTo(expected));
            Assert.That(PulletYooAssetPlatform.FromRuntimePlatform(runtimePlatform),
                Is.EqualTo(expected));
        }

        [TestCase(null, null, "1.0.0", EPulletYooAssetAutomaticVersionDecision.UseRemote)]
        [TestCase("1.0.0", null, "1.0.0", EPulletYooAssetAutomaticVersionDecision.KeepCurrent)]
        [TestCase("1.0.0", null, "1.0.1", EPulletYooAssetAutomaticVersionDecision.UseRemote)]
        [TestCase("1.0.1", null, "1.0.0", EPulletYooAssetAutomaticVersionDecision.KeepCurrent)]
        [TestCase("1.0.0", "1.0.2", "1.0.1", EPulletYooAssetAutomaticVersionDecision.PreferBuiltin)]
        [TestCase("1.0.0", "1.0.2", "1.0.2", EPulletYooAssetAutomaticVersionDecision.UseRemote)]
        [TestCase("1.0.3", "1.0.2", "1.0.1", EPulletYooAssetAutomaticVersionDecision.KeepCurrent)]
        [TestCase("1.0.0", null, "2026-09-14-174738", EPulletYooAssetAutomaticVersionDecision.Incomparable)]
        [TestCase("2026-09-14-174738", null, "2026-09-15-010000", EPulletYooAssetAutomaticVersionDecision.UseRemote)]
        [TestCase("1.0.0", null, "beta", EPulletYooAssetAutomaticVersionDecision.Incomparable)]
        public void AutomaticVersionSelectionHonorsBaseline(
            string current, string builtin, string remote,
            EPulletYooAssetAutomaticVersionDecision expected)
        {
            var decision = PulletYooAssetVersion.SelectAutomatic(
                current, builtin, remote, out string reason);

            Assert.That(decision, Is.EqualTo(expected), reason);
        }

        [TestCase("1.0.2", "1.0.1", false)]
        [TestCase("1.0.1", "1.0.1", true)]
        [TestCase(null, "1.0.1", true)]
        public void BuiltinFallbackNeverDowngradesAnActiveManifest(
            string current, string builtin, bool expected)
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<PulletYooAssetSettings>();
            try
            {
                settings.packageName = "DefaultPackage";
                settings.includeDefaultPackageInStreamingAssets = true;
                settings.builtinDefaultPackageVersion = builtin;
                settings.editorPlayMode = EPulletYooAssetPlayMode.Web;
                var state = new PulletYooAssetPackageState("DefaultPackage")
                {
                    CurrentVersion = current
                };
                var operation = new PulletYooAssetPackageOperation(
                    "DefaultPackage", EPulletYooAssetOperationType.Prepare);
                var request = new PulletYooAssetPipelineRequest(
                    "DefaultPackage", EPulletYooAssetOperationType.Prepare);

                Assert.That(PulletYooAssetPackagePipeline.CanFallbackToBuiltin(
                    request, settings, state, operation), Is.EqualTo(expected));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [TestCase(EPulletYooAssetOperationType.Prepare, "1.0.0", true)]
        [TestCase(EPulletYooAssetOperationType.Prepare, null, false)]
        [TestCase(EPulletYooAssetOperationType.Check, "1.0.0", false)]
        [TestCase(EPulletYooAssetOperationType.Update, "1.0.0", false)]
        [TestCase(EPulletYooAssetOperationType.Download, "1.0.0", false)]
        public void OnlyAutomaticPrepareCanReuseCurrentManifestWhenRemoteFails(
            EPulletYooAssetOperationType operationType, string currentVersion, bool expected)
        {
            var request = new PulletYooAssetPipelineRequest("DefaultPackage", operationType);
            var state = new PulletYooAssetPackageState("DefaultPackage")
            {
                CurrentVersion = currentVersion
            };

            Assert.That(PulletYooAssetPackagePipeline.CanReuseCurrentManifest(request, state),
                Is.EqualTo(expected));
        }
    }
}
