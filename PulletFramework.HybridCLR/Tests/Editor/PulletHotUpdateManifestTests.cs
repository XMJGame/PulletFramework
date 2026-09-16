using System.Collections.Generic;
using NUnit.Framework;

namespace PulletFramework.HybridCLR.Tests
{
    public sealed class PulletHotUpdateManifestTests
    {
        [Test]
        public void JsonRoundTripPreservesCompatibilityAndAssembly()
        {
            var manifest = new PulletHotUpdateManifest
            {
                contentVersion = "7",
                compatiblePlayerVersions = new[] { "1.0.0" },
                entryAssembly = "Game.HotUpdate",
                entryType = "Game.Entry",
                entryMethod = "Run",
                assemblies = new[]
                {
                    Entry("Game.HotUpdate", "Assets/Game.HotUpdate.dll.bytes")
                }
            };

            PulletHotUpdateManifest restored = PulletHotUpdateManifest.FromJson(manifest.ToJson());

            Assert.That(restored.contentVersion, Is.EqualTo("7"));
            Assert.That(restored.SupportsPlayerVersion("1.0.0"), Is.True);
            Assert.That(restored.entryType, Is.EqualTo("Game.Entry"));
            Assert.That(restored.assemblies[0].name, Is.EqualTo("Game.HotUpdate"));
        }

        [Test]
        public void ValidateRejectsIncompatiblePlayer()
        {
            var manifest = new PulletHotUpdateManifest
            {
                compatiblePlayerVersions = new[] { "2.0.0" },
                entryAssembly = "Game.HotUpdate",
                entryType = "Game.Entry",
                entryMethod = "Run"
            };

            string error = PulletHotUpdate.ValidateAndSort(manifest, "1.0.0", out _);

            Assert.That(error, Does.Contain("不适用于 Player"));
        }

        [Test]
        public void ValidateSortsHotUpdateDependencies()
        {
            PulletHotUpdateAssemblyInfo feature = Entry("Feature", "Feature.bytes");
            feature.dependencies = new[] { "Foundation" };
            PulletHotUpdateManifest manifest = Manifest(
                feature, Entry("Foundation", "Foundation.bytes"));

            string error = PulletHotUpdate.ValidateAndSort(
                manifest, "1.0.0", out List<PulletHotUpdateAssemblyInfo> ordered);

            Assert.That(error, Is.Null);
            Assert.That(ordered[0].name, Is.EqualTo("Foundation"));
            Assert.That(ordered[1].name, Is.EqualTo("Feature"));
        }

        [Test]
        public void ValidateRejectsDependencyCycle()
        {
            PulletHotUpdateAssemblyInfo first = Entry("First", "First.bytes");
            PulletHotUpdateAssemblyInfo second = Entry("Second", "Second.bytes");
            first.dependencies = new[] { "Second" };
            second.dependencies = new[] { "First" };
            PulletHotUpdateManifest manifest = Manifest(first, second);

            string error = PulletHotUpdate.ValidateAndSort(manifest, "1.0.0", out _);

            Assert.That(error, Does.Contain("循环依赖"));
        }

        [Test]
        public void ValidateRejectsMissingHotUpdateDependency()
        {
            PulletHotUpdateAssemblyInfo feature = Entry("Feature", "Feature.bytes");
            feature.dependencies = new[] { "Foundation" };
            PulletHotUpdateManifest manifest = Manifest(feature);

            string error = PulletHotUpdate.ValidateAndSort(manifest, "1.0.0", out _);

            Assert.That(error, Does.Contain("缺少依赖"));
        }

        [Test]
        public void ValidateRejectsDuplicateAcrossAotAndHotUpdate()
        {
            PulletHotUpdateAssemblyInfo aot = Entry("Foundation", "AOT.bytes");
            aot.kind = EPulletHotUpdateAssemblyKind.AotMetadata;
            PulletHotUpdateManifest manifest = Manifest(
                aot, Entry("Foundation", "Hot.bytes"));

            string error = PulletHotUpdate.ValidateAndSort(manifest, "1.0.0", out _);

            Assert.That(error, Does.Contain("重复程序集"));
        }

        private static PulletHotUpdateAssemblyInfo Entry(string name, string location)
        {
            return new PulletHotUpdateAssemblyInfo
            {
                name = name,
                location = location,
                kind = EPulletHotUpdateAssemblyKind.HotUpdate,
                byteLength = 1,
                sha256 = new string('0', 64)
            };
        }

        private static PulletHotUpdateManifest Manifest(
            params PulletHotUpdateAssemblyInfo[] assemblies)
        {
            return new PulletHotUpdateManifest
            {
                entryAssembly = assemblies.Length > 0
                    ? assemblies[assemblies.Length - 1].name
                    : "Game.HotUpdate",
                entryType = "Game.Entry",
                entryMethod = "Run",
                assemblies = assemblies
            };
        }
    }
}
