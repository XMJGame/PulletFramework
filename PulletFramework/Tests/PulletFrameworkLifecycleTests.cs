using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.Tests
{
    public sealed class PulletFrameworkLifecycleTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PulletFrameworks.Destroy();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingDriverResetsFrameworkAndAllowsRestart()
        {
            PulletFrameworks.Initialize();
            Assert.That(PulletFrameworks.IsInitialized, Is.True);

            Object.Destroy(PulletFrameworks.mono.gameObject);
            yield return null;

            Assert.That(PulletFrameworks.IsInitialized, Is.False);
            Assert.DoesNotThrow(PulletFrameworks.Initialize);
            Assert.That(PulletFrameworks.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedInitializeAndDestroyIsSafe()
        {
            for (int i = 0; i < 10; i++)
            {
                PulletFrameworks.Initialize();
                Assert.That(PulletFrameworks.IsInitialized, Is.True);
                PulletFrameworks.Destroy();
                Assert.That(PulletFrameworks.IsInitialized, Is.False);
            }
            PulletFrameworks.Destroy();
            yield return null;

            Assert.That(PulletFrameworks.IsInitialized, Is.False);
        }
    }
}
