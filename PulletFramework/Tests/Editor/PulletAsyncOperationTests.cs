using System;
using NUnit.Framework;

namespace PulletFramework.Tests
{
    public sealed class PulletAsyncOperationTests
    {
        private sealed class TestOperation : PulletAsyncOperation
        {
            private readonly bool _completeOnUpdate;

            public bool WasAborted { get; private set; }

            public TestOperation(bool completeOnUpdate = false)
            {
                _completeOnUpdate = completeOnUpdate;
            }

            protected override void OnStart() { }

            protected override void OnUpdate()
            {
                if (_completeOnUpdate)
                    SetSucceeded();
            }

            protected override void OnAbort()
            {
                WasAborted = true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            PLogger.Level = EPulletLogLevel.Off;
            PulletOperationSystem.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PulletOperationSystem.Clear();
            PLogger.Level = EPulletLogLevel.Info;
        }

        [Test]
        public void CompletedCallbackCanClearScheduler()
        {
            TestOperation pending = PulletOperationSystem.Start(new TestOperation());
            TestOperation completing = PulletOperationSystem.Start(new TestOperation(true));
            completing.Completed += _ => PulletOperationSystem.Clear();

            Assert.DoesNotThrow(PulletOperationSystem.Update);
            Assert.That(completing.Status, Is.EqualTo(EPulletOperationStatus.Succeeded));
            Assert.That(pending.IsDone, Is.True);
            Assert.That(pending.WasAborted, Is.True);
        }

        [Test]
        public void ClearContinuesWhenCompletionListenerThrows()
        {
            bool secondListenerCalled = false;
            TestOperation first = PulletOperationSystem.Start(new TestOperation());
            TestOperation second = PulletOperationSystem.Start(new TestOperation());
            second.Completed += _ => throw new InvalidOperationException("Expected test failure.");
            second.Completed += _ => secondListenerCalled = true;

            Assert.DoesNotThrow(PulletOperationSystem.Clear);
            Assert.That(first.IsDone, Is.True);
            Assert.That(second.IsDone, Is.True);
            Assert.That(secondListenerCalled, Is.True);
        }

        [Test]
        public void OperationStartedDuringClearIsImmediatelyAborted()
        {
            TestOperation spawned = null;
            TestOperation operation = PulletOperationSystem.Start(new TestOperation());
            operation.Completed += _ => spawned = PulletOperationSystem.Start(new TestOperation());

            PulletOperationSystem.Clear();

            Assert.That(spawned, Is.Not.Null);
            Assert.That(spawned.IsDone, Is.True);
            Assert.That(spawned.WasAborted, Is.True);
        }
    }
}
