using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PulletFramework.Form;
using PulletFramework.Resource;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.Tests
{
    public sealed class PulletFormTests
    {
        private TestPackage _package;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PulletFrameworks.Destroy();
            PulletResources.Uninstall();
            yield return null;
            PulletFrameworks.Initialize();
            _package = new TestPackage();
            PulletResources.Install(new TestAdapter(_package));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PulletFrameworks.Destroy();
            PulletResources.Uninstall();
            _package?.DestroyAssets();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedParseIsReportedAndCanBeRetried()
        {
            _package.Enqueue("Id\tValue\n1\tinvalid");
            LogAssert.Expect(LogType.Error, new Regex("解析表格失败"));
            PulletForm.AddForm<TestForm>();
            while (!PulletForm.IsReadComplete) yield return null;
            Assert.That(PulletForm.IsReadSuccessful, Is.False);
            Assert.That(PulletForm.Failures.Count, Is.EqualTo(1));
            Assert.That(PulletForm.Failures[0].FormType, Is.EqualTo(typeof(TestForm)));
            StringAssert.Contains("解析失败", PulletForm.Failures[0].Error);

            _package.Enqueue("Id\tValue\n1\t42");
            Assert.That(PulletForm.RetryFailedForms(), Is.EqualTo(1));
            while (!PulletForm.IsReadComplete) yield return null;
            Assert.That(PulletForm.IsReadSuccessful, Is.True);
            Assert.That(TestForm.Instance.GetDateById(1).Value, Is.EqualTo(42));
        }

        [UnityTest]
        public IEnumerator DestroyDuringReadDoesNotRestoreOldState()
        {
            _package.Enqueue("Id\tValue\n1\t7", 4);
            PulletForm.AddForm<TestForm>();
            Assert.That(PulletForm.readCount, Is.EqualTo(1));
            PulletForm.Destroy();
            for (int i = 0; i < 6; i++) yield return null;
            Assert.That(PulletForm.readCount, Is.Zero);
            Assert.That(PulletForm.Failures.Count, Is.Zero);
            Assert.That(TestForm.Instance, Is.Null);

            _package.Enqueue("Id\tValue\n1\t8");
            PulletForm.AddForm<TestForm>();
            while (!PulletForm.IsReadComplete) yield return null;
            Assert.That(TestForm.Instance.GetDateById(1).Value, Is.EqualTo(8));
        }

        public sealed class TestForm : FormSingleton<TestForm, TestRow>
        {
            public override string formPath => "Tests/TestForm";
        }

        public sealed class TestRow { public int Id; public int Value; }

        private sealed class TestAssetHandle : IResourceAssetHandle
        {
            private readonly UnityEngine.Object _asset;
            private int _remainingFrames;
            public TestAssetHandle(UnityEngine.Object asset, int delayFrames)
            {
                _asset = asset;
                _remainingFrames = delayFrames;
            }
            public bool IsValid => true;
            public bool IsDone { get; private set; }
            public bool IsSucceeded => IsDone && _asset != null;
            public string Error => IsSucceeded ? null : "Test asset is unavailable.";
            public UnityEngine.Object AssetObject => IsDone ? _asset : null;
            public object Current => null;
            public event Action<IResourceAssetHandle> Completed;
            public bool MoveNext()
            {
                if (IsDone) return false;
                if (_remainingFrames-- > 0) return true;
                IsDone = true;
                Completed?.Invoke(this);
                return false;
            }
            public void Reset() { }
            public void WaitForCompletion() { while (MoveNext()) { } }
            public GameObject InstantiateSync(ResourceInstantiateOptions options) => null;
            public IResourceInstanceHandle InstantiateAsync(ResourceInstantiateOptions options) => null;
            public void Release() { }
        }

        private sealed class TestPackage : IResourcePackage
        {
            private readonly Queue<TestAssetHandle> _handles = new Queue<TestAssetHandle>();
            private readonly List<TextAsset> _assets = new List<TextAsset>();
            public string Name => "DefaultPackage";
            public EResourcePackageStatus Status => EResourcePackageStatus.Succeeded;
            public string Error => null;
            public bool IsLocationValid(string location) => true;
            public void Enqueue(string text, int delayFrames = 0)
            {
                var asset = new TextAsset(text);
                _assets.Add(asset);
                _handles.Enqueue(new TestAssetHandle(asset, delayFrames));
            }
            public IResourceAssetHandle LoadAssetAsync<TObject>(string location)
                where TObject : UnityEngine.Object => _handles.Dequeue();
            public void DestroyAssets()
            {
                for (int i = 0; i < _assets.Count; i++) UnityEngine.Object.Destroy(_assets[i]);
            }
        }

        private sealed class TestAdapter : IResourceAdapter
        {
            private readonly IResourcePackage _package;
            public TestAdapter(IResourcePackage package) { _package = package; }
            public string Name => "FormTests";
            public string DefaultPackageName => "DefaultPackage";
            public bool TryGetPackage(string packageName, out IResourcePackage package)
            {
                package = _package;
                return true;
            }
            public IResourcePackage GetPackage(string packageName) => _package;
        }
    }
}
