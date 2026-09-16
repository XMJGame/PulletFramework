using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletFramework.AssetPublishing.Editor;
using PulletFramework.Editor;

namespace PulletFramework.YooAssetAdapter.Tests
{
    public sealed class PulletYooAssetPublishingTests
    {
        private sealed class TestProvider : IPulletObjectStorageProvider
        {
            private readonly Func<string, string, Task<string>> _upload;

            public string Id => "test";
            public string DisplayName => "test";

            public TestProvider(Func<string, string, Task<string>> upload = null)
            {
                _upload = upload;
            }

            public void ApplyDefaults(PulletAssetPublishingProfile profile) { }
            public bool Validate(PulletAssetPublishingProfile profile, out string message)
            {
                message = null;
                return true;
            }

            public string BuildPublicUrl(PulletAssetPublishingProfile profile, string objectKey)
            {
                return profile.publicBaseUrl.TrimEnd('/') + "/" + objectKey;
            }

            public Task<string> UploadAsync(PulletAssetPublishingProfile profile,
                string objectKey, string sourcePath, Action<long, long> progress = null,
                string cacheControl = null, string contentType = null)
            {
                return _upload == null
                    ? Task.FromResult(BuildPublicUrl(profile, objectKey))
                    : _upload(objectKey, BuildPublicUrl(profile, objectKey));
            }

            public bool EnsureMiniGameDownloadCors(PulletAssetPublishingProfile profile)
            {
                return false;
            }
        }

        [Test]
        public void ProviderDiscoverySkipsTypesWithoutDefaultConstructor()
        {
            List<IPulletObjectStorageProvider> providers =
                PulletAssetPublishingProviderRegistry
                    .CreateProviders<IPulletObjectStorageProvider>();

            Assert.That(providers.Any(provider => provider is TestProvider), Is.False);
        }

        [Test]
        public void SessionFreezesProfileAndBlocksDuplicateDestination()
        {
            var profile = new PulletAssetPublishingProfile
            {
                publicBaseUrl = "https://example.com/original"
            };
            var session = new PulletAssetPublishingSession(new TestProvider(), profile);
            profile.publicBaseUrl = "https://example.com/changed";

            Assert.That(session.BuildPublicUrl("assets/file.bundle"),
                Is.EqualTo("https://example.com/original/assets/file.bundle"));
            IDisposable first = session.AcquirePublication("assets/package");
            try
            {
                Assert.Throws<InvalidOperationException>(
                    () => session.AcquirePublication("assets/package"));
                Assert.Throws<InvalidOperationException>(
                    () => session.AcquirePublication("assets/another-package"));
            }
            finally
            {
                first.Dispose();
            }
            Assert.DoesNotThrow(() => session.AcquirePublication("assets/package").Dispose());
        }

        [Test]
        public void ReportRejectsChangedPayloadBeforePublishingPointer()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "PulletPublishingTests-" + Guid.NewGuid().ToString("N"));
            string packageDirectory = Path.Combine(root, "DefaultPackage", "1.0.0");
            Directory.CreateDirectory(packageDirectory);
            try
            {
                File.WriteAllText(Path.Combine(packageDirectory, "DefaultPackage.version"), "1.0.0");
                File.WriteAllText(Path.Combine(packageDirectory,
                    "DefaultPackage_1.0.0.bytes"), "manifest");
                File.WriteAllText(Path.Combine(packageDirectory,
                    "DefaultPackage_1.0.0.hash"), "hash");
                string payload = Path.Combine(packageDirectory, "file.bundle");
                File.WriteAllText(payload, "original");

                string reportPath = PulletYooAssetPublishReport.Create(
                    packageDirectory, "DefaultPackage", "1.0.0");
                var report = PulletYooAssetPublishReport.Load(reportPath);
                Assert.DoesNotThrow(() => PulletYooAssetPublishReport.Validate(
                    report, packageDirectory, "DefaultPackage", "1.0.0"));

                File.WriteAllText(payload, "modified");
                Assert.Throws<InvalidDataException>(() => PulletYooAssetPublishReport.Validate(
                    report, packageDirectory, "DefaultPackage", "1.0.0"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ManifestFailureDoesNotPublishVersionPointer()
        {
            string root = CreatePackage(out string packageDirectory, out string reportPath);
            var uploaded = new List<string>();
            var provider = new TestProvider((key, url) =>
            {
                uploaded.Add(key);
                if (key.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Expected manifest upload failure.");
                return Task.FromResult(url);
            });
            var session = CreateSession(provider);
            try
            {
                var report = PulletYooAssetPublishReport.Load(reportPath);

                IOException failure = null;
                try
                {
                    PulletYooAssetCosPublisher.UploadReportAsync(
                        session, report, packageDirectory,
                        "game-assets/WebGL/1.0.0/v1/DefaultPackage")
                        .GetAwaiter().GetResult();
                }
                catch (IOException exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.Not.Null);
                Assert.That(uploaded.Exists(key =>
                    key.EndsWith(".version", StringComparison.OrdinalIgnoreCase)), Is.False);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ChangedPayloadDuringUploadStopsBeforeVersionPointer()
        {
            string root = CreatePackage(out string packageDirectory, out string reportPath);
            var uploaded = new List<string>();
            var provider = new TestProvider((key, url) =>
            {
                uploaded.Add(key);
                return Task.FromResult(url);
            });
            var session = CreateSession(provider);
            try
            {
                var report = PulletYooAssetPublishReport.Load(reportPath);
                string payload = Path.Combine(packageDirectory, "file.bundle");

                InvalidDataException failure = null;
                try
                {
                    PulletYooAssetCosPublisher.UploadReportAsync(
                        session, report, packageDirectory,
                        "game-assets/WebGL/1.0.0/v1/DefaultPackage",
                        (file, _, __) =>
                        {
                            if (file.uploadPhase == 3)
                                File.WriteAllText(payload, "changed during upload");
                        }).GetAwaiter().GetResult();
                }
                catch (InvalidDataException exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.Not.Null);
                Assert.That(uploaded.Exists(key =>
                    key.EndsWith(".version", StringComparison.OrdinalIgnoreCase)), Is.False);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static PulletAssetPublishingSession CreateSession(TestProvider provider)
        {
            return new PulletAssetPublishingSession(provider,
                new PulletAssetPublishingProfile
                {
                    publicBaseUrl = "https://example.com"
                });
        }

        private static string CreatePackage(
            out string packageDirectory, out string reportPath)
        {
            string root = Path.Combine(Path.GetTempPath(),
                "PulletPublishingTests-" + Guid.NewGuid().ToString("N"));
            packageDirectory = Path.Combine(root, "DefaultPackage", "1.0.0");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(Path.Combine(packageDirectory, "DefaultPackage.version"), "1.0.0");
            File.WriteAllText(Path.Combine(packageDirectory,
                "DefaultPackage_1.0.0.bytes"), "manifest");
            File.WriteAllText(Path.Combine(packageDirectory,
                "DefaultPackage_1.0.0.hash"), "hash");
            File.WriteAllText(Path.Combine(packageDirectory, "file.bundle"), "payload");
            reportPath = PulletYooAssetPublishReport.Create(
                packageDirectory, "DefaultPackage", "1.0.0");
            return root;
        }
    }
}
