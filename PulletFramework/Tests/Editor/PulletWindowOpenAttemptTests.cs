using NUnit.Framework;
using PulletFramework.Window;

namespace PulletFramework.Tests
{
    public sealed class PulletWindowOpenAttemptTests
    {
        private sealed class TestWindow : UIWindow
        {
            public override string assetPath => string.Empty;
        }

        [SetUp]
        public void SetUp()
        {
            PulletOperationSystem.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PulletOperationSystem.Clear();
        }

        [Test]
        public void OldOperationFailsWhenANewerAttemptStarts()
        {
            var window = new TestWindow();
            window.Init("Test", EWindowLayer.NormalLayer);
            long oldAttempt = window.InternalBeginOpenAttempt();
            var operation = PulletOperationSystem.Start(
                new OpenWindowOperation(window, openAttemptId: oldAttempt));

            window.InternalBeginOpenAttempt();
            operation.UpdateInternal();

            Assert.That(operation.Status, Is.EqualTo(EPulletOperationStatus.Failed));
            StringAssert.Contains("superseded", operation.Error);
        }

        [Test]
        public void FollowerOperationFailsWhenItsAttemptIsCancelled()
        {
            var window = new TestWindow();
            window.Init("Test", EWindowLayer.NormalLayer);
            long attempt = window.InternalBeginOpenAttempt();
            var operation = PulletOperationSystem.Start(
                new OpenWindowOperation(window, openAttemptId: attempt));

            Assert.That(window.InternalCancelOpenAttempt(attempt), Is.True);
            operation.UpdateInternal();

            Assert.That(operation.Status, Is.EqualTo(EPulletOperationStatus.Failed));
            StringAssert.Contains("cancelled", operation.Error);
        }

        [Test]
        public void StaleCancellationCannotCancelCurrentAttempt()
        {
            var window = new TestWindow();
            window.Init("Test", EWindowLayer.NormalLayer);
            long staleAttempt = window.InternalBeginOpenAttempt();
            long currentAttempt = window.InternalBeginOpenAttempt();

            Assert.That(window.InternalCancelOpenAttempt(staleAttempt), Is.False);
            Assert.That(window.GetOpenAttemptError(currentAttempt), Is.Null);
        }
    }
}
