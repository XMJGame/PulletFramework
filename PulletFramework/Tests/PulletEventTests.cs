using System;
using System.Collections;
using NUnit.Framework;
using PulletFramework.Event;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.Tests
{
    public sealed class PulletEventTests
    {
        private EPulletLogLevel level;
        private sealed class Message : IEventMessage { }
        [SetUp]
        public void Setup() { level = PLogger.Level; PulletEvent.Destroy(); PulletEvent.Initialize(); PLogger.Level = EPulletLogLevel.Off; }
        [TearDown]
        public void Cleanup() { PulletEvent.Destroy(); PulletFrameworks.Destroy(); PLogger.Level = level; }

        [Test]
        public void SelfRemovalDoesNotSkipEarlierListeners()
        {
            int calls = 0;
            Action<IEventMessage> self = null;
            self = _ => PulletEvent.RemoveListener(1, self);
            PulletEvent.AddListener(1, _ => calls++);
            PulletEvent.AddListener(1, self);
            PulletEvent.SendMessage(1, new Message());
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void RemovalAndReplacementDoNotJoinTheCurrentSnapshot()
        {
            int calls = 0;
            Action<IEventMessage> previous = _ => calls++;
            Action<IEventMessage> replace = _ => { PulletEvent.RemoveListener(1, previous); PulletEvent.AddListener(1, previous); };
            PulletEvent.AddListener(1, previous); PulletEvent.AddListener(1, replace);
            PulletEvent.SendMessage(1, new Message()); Assert.AreEqual(0, calls);
            PulletEvent.RemoveListener(1, replace);
            PulletEvent.SendMessage(1, new Message()); Assert.AreEqual(1, calls);
        }

        [Test]
        public void NestedSendGetsItsOwnCurrentSnapshot()
        {
            int calls = 0; bool nested = false;
            PulletEvent.AddListener(1, _ => calls++);
            PulletEvent.AddListener(1, _ => { if (!nested) { nested = true; PulletEvent.SendMessage(1, new Message()); } });
            PulletEvent.SendMessage(1, new Message()); Assert.AreEqual(2, calls);
        }

        [Test]
        public void ClearDuringSendCancelsRemainingListeners()
        {
            int calls = 0;
            PulletEvent.AddListener(1, _ => calls++); PulletEvent.AddListener(1, _ => PulletEvent.ClearAll());
            PulletEvent.SendMessage(1, new Message()); Assert.AreEqual(0, calls);
        }

        [Test]
        public void GroupCleanupDoesNotReinitializeClosedEvents()
        {
            var group = new EventGroup(); group.AddListener<Message>(_ => { });
            PulletEvent.Destroy(); group.RemoveAllListener();
            Assert.IsNull(PulletEvent.gameObject);
        }

        [UnityTest]
        public IEnumerator DeferredFailuresAreConsumedOnceAndDoNotBlockOtherEvents()
        {
            PLogger.Level = EPulletLogLevel.Info;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Event listener failed: 2"));
            int failures = 0, healthy = 0;
            PulletEvent.AddListener(1, _ => healthy++);
            PulletEvent.AddListener(2, _ => { failures++; throw new InvalidOperationException("Expected listener failure"); });
            PulletEvent.PostMessage(1, new Message()); PulletEvent.PostMessage(2, new Message());
            yield return null; PulletEvent.Update(0, 0);
            yield return null; PulletEvent.Update(0, 0);
            Assert.AreEqual(1, failures); Assert.AreEqual(1, healthy);
        }

        [UnityTest]
        public IEnumerator ClearDuringDeferredDispatchCancelsTheRestWithoutIndexMutation()
        {
            int calls = 0;
            PulletEvent.AddListener(1, _ => calls++);
            PulletEvent.AddListener(2, _ => PulletEvent.ClearAll());
            PulletEvent.PostMessage(1, new Message()); PulletEvent.PostMessage(2, new Message());
            yield return null;
            Assert.DoesNotThrow(() => PulletEvent.Update(0, 0));
            Assert.AreEqual(0, calls);
        }

        [UnityTest]
        public IEnumerator PostsCreatedByCallbacksWaitUntilAnotherFrame()
        {
            int calls = 0;
            PulletEvent.AddListener(1, _ => { calls++; if (calls == 1) PulletEvent.PostMessage(1, new Message()); });
            PulletEvent.PostMessage(1, new Message());
            yield return null; PulletEvent.Update(0, 0); Assert.AreEqual(1, calls);
            PulletEvent.Update(0, 0); Assert.AreEqual(1, calls);
            yield return null; PulletEvent.Update(0, 0); Assert.AreEqual(2, calls);
        }

        [UnityTest]
        public IEnumerator DestroyAndReinitializeCannotDeliverAnOldDeferredBatch()
        {
            int calls = 0;
            PulletEvent.AddListener(1, _ => calls++);
            PulletEvent.AddListener(2, _ => { PulletEvent.Destroy(); PulletEvent.Initialize(); });
            PulletEvent.PostMessage(1, new Message()); PulletEvent.PostMessage(2, new Message());
            yield return null; PulletEvent.Update(0, 0);
            Assert.AreEqual(0, calls);
        }
    }
}
