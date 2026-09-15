using System;
using NUnit.Framework;

namespace PulletFramework.YooAssetAdapter.Tests
{
    public sealed class PulletYooAssetPackageOperationTests
    {
        [SetUp]
        public void SetUp()
        {
            PLogger.Level = EPulletLogLevel.Off;
        }

        [TearDown]
        public void TearDown()
        {
            PLogger.Level = EPulletLogLevel.Info;
        }

        [Test]
        public void ListenerFailureDoesNotBlockRemainingListeners()
        {
            var operation = new PulletYooAssetPackageOperation(
                "DefaultPackage", EPulletYooAssetOperationType.Prepare);
            int progressCount = 0;
            int completionCount = 0;
            operation.ProgressChanged += _ =>
                throw new InvalidOperationException("Expected progress listener failure.");
            operation.ProgressChanged += _ => progressCount++;
            operation.Completed += _ =>
                throw new InvalidOperationException("Expected completion listener failure.");
            operation.Completed += _ => completionCount++;

            Assert.DoesNotThrow(() => operation.ReportProgress(0.5f));
            operation.SetSucceeded("1.0.0");
            Assert.DoesNotThrow(() => operation.NotifyCompleted(true));

            Assert.That(progressCount, Is.EqualTo(2));
            Assert.That(completionCount, Is.EqualTo(1));
        }

        [Test]
        public void PipelineReleasesOwnershipBeforeCompletionCallback()
        {
            var operation = new PulletYooAssetPackageOperation(
                "DefaultPackage", EPulletYooAssetOperationType.Prepare);
            bool occupied = true;
            bool callbackSawReleasedState = false;
            operation.Completed += _ => callbackSawReleasedState = !occupied;
            operation.SetSucceeded("1.0.0");

            PulletYooAssetPackagePipeline.FinishAndNotify(
                operation, _ => occupied = false, false, true);

            Assert.That(callbackSawReleasedState, Is.True);
        }

        [Test]
        public void LateCompletionSubscriberRunsExactlyOnce()
        {
            var operation = new PulletYooAssetPackageOperation(
                "DefaultPackage", EPulletYooAssetOperationType.Prepare);
            operation.SetSucceeded("1.0.0");
            operation.NotifyCompleted();
            int completionCount = 0;

            operation.Completed += _ => completionCount++;
            operation.NotifyCompleted();

            Assert.That(completionCount, Is.EqualTo(1));
        }
    }
}
