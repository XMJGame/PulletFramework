using System.Collections;
using NUnit.Framework;
using PulletFramework.Resource;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.YooAssetAdapter.Tests
{
    public sealed class PulletYooAssetsResetTests
    {
        [SetUp]
        [TearDown]
        public void ResetRuntime()
        {
            PulletResources.Uninstall();
            PulletYooAssets.Reset(true);
        }

        [UnityTest]
        public IEnumerator ResetImmediatelyCompletesActiveHandleAsCancelled()
        {
            Assert.That(PulletYooAssets.IsConfigured, Is.True,
                "The validation project must provide PulletYooAssetSettings.");
            PulletYooAssetPackageOperation operation =
                PulletYooAssets.InitializePackageAsync(PulletYooAssets.DefaultPackageName);
            int completionCount = 0;
            operation.Completed += _ => completionCount++;

            PulletYooAssets.Reset(false);

            Assert.That(operation.IsDone, Is.True);
            Assert.That(operation.IsCancelled, Is.True);
            Assert.That(operation.keepWaiting, Is.False);
            Assert.That(completionCount, Is.EqualTo(1));
            yield return null;
            Assert.That(completionCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RepeatedPrepareKeepsActivePackageReady()
        {
            Assert.That(PulletYooAssets.IsConfigured, Is.True);
            PulletYooAssetSettings settings = PulletYooAssetSettingsData.Setting;
            bool originalWarmup = settings.warmupShaderVariantsOnPrepare;
            settings.warmupShaderVariantsOnPrepare = false;
            try
            {
                string packageName = PulletYooAssets.DefaultPackageName;
                PulletYooAssetPackageOperation first =
                    PulletYooAssets.PreparePackageAsync(packageName, false);
                yield return WaitForOperation(first);
                Assert.That(first.Succeeded, Is.True, first.Error);
                string version = PulletYooAssets.GetPackageState(packageName).CurrentVersion;
                Assert.That(version, Is.Not.Empty);

                PulletYooAssetPackageOperation second =
                    PulletYooAssets.PreparePackageAsync(packageName, false);
                yield return WaitForOperation(second);

                Assert.That(second.Succeeded, Is.True, second.Error);
                Assert.That(PulletYooAssets.IsPackageReady(packageName), Is.True);
                Assert.That(PulletYooAssets.GetPackageState(packageName).CurrentVersion,
                    Is.EqualTo(version));
            }
            finally
            {
                settings.warmupShaderVariantsOnPrepare = originalWarmup;
            }
        }

        private static IEnumerator WaitForOperation(PulletYooAssetPackageOperation operation)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!operation.IsDone && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(operation.IsDone, Is.True,
                $"资源操作超时：{operation.PackageName}，"
                + $"状态={PulletYooAssets.GetPackageState(operation.PackageName).Status}，"
                + $"进度={operation.Progress:P0}。请检查模拟模式的 YooAsset 驱动和清单初始化。");
        }
    }
}
