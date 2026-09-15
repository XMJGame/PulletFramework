using System;
using NUnit.Framework;
using PulletFramework.Resource;

namespace PulletFramework.Tests
{
    public sealed class PulletResourcesTests
    {
        private sealed class TestAdapter : IResourceAdapter
        {
            public string Name { get; }
            public string DefaultPackageName => "DefaultPackage";

            public TestAdapter(string name)
            {
                Name = name;
            }

            public bool TryGetPackage(string packageName, out IResourcePackage package)
            {
                package = null;
                return false;
            }

            public IResourcePackage GetPackage(string packageName)
            {
                throw new InvalidOperationException("No package is required by this test.");
            }
        }

        [SetUp]
        [TearDown]
        public void ResetAdapter()
        {
            PulletResources.Uninstall();
        }

        [Test]
        public void InstallingSameAdapterIsIdempotent()
        {
            var adapter = new TestAdapter("first");

            PulletResources.Install(adapter);

            Assert.DoesNotThrow(() => PulletResources.Install(adapter));
            Assert.That(PulletResources.Adapter, Is.SameAs(adapter));
        }

        [Test]
        public void ReplacingAdapterRequiresExplicitUninstall()
        {
            var first = new TestAdapter("first");
            var second = new TestAdapter("second");
            PulletResources.Install(first);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PulletResources.Install(second));

            StringAssert.Contains("Uninstall", exception.Message);
            Assert.That(PulletResources.Adapter, Is.SameAs(first));
        }
    }
}
