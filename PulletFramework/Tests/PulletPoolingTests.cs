using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using PulletFramework.Pooling;
using PulletFramework.Resource;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.Tests
{
    public sealed class PulletPoolingTests
    {
        private TestPackage _package;
        private Spawner _spawner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PulletFrameworks.Destroy();
            PulletResources.Uninstall();
            yield return null;
            PulletFrameworks.Initialize();
            _package = new TestPackage();
            PulletResources.Install(new TestAdapter(_package));
            _spawner = PulletPooling.CreateSpawner(_package.Name);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PulletFrameworks.Destroy();
            PulletResources.Uninstall();
            _package?.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestoreDuringLoadCompletesHandleAsCancelled()
        {
            _package.InstanceDelayFrames = 4;
            SpawnHandle handle = _spawner.SpawnAsync("Test/Prefab");
            int completedCount = 0;
            handle.Completed += _ => completedCount++;

            handle.Restore();
            yield return null;

            Assert.That(handle.Status, Is.EqualTo(EPulletOperationStatus.Failed));
            StringAssert.Contains("cancelled", handle.Error);
            Assert.That(completedCount, Is.EqualTo(1));
            Assert.That(_package.LastInstance.CancelCount, Is.GreaterThanOrEqualTo(1));

            handle.Restore();
            Assert.That(completedCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyedSpawnerIsTerminal()
        {
            Assert.That(PulletPooling.DestroySpawner(_package.Name), Is.True);
            Assert.That(_spawner.IsDestroyed, Is.True);
            Assert.That(_spawner.PoolCount, Is.Zero);

            Assert.Throws<ObjectDisposedException>(() =>
                _spawner.SpawnAsync("Test/Prefab"));
            Assert.Throws<ObjectDisposedException>(() =>
                _spawner.CreateGameObjectPoolAsync("Test/Prefab"));

            Spawner replacement = PulletPooling.CreateSpawner(_package.Name);
            Assert.That(replacement, Is.Not.SameAs(_spawner));
            Assert.That(replacement.IsDestroyed, Is.False);
        }

        [Test]
        public void CompletedInstanceUsesImmediatePath()
        {
            _package.InstanceDelayFrames = 0;
            SpawnHandle handle = _spawner.SpawnAsync("Test/Immediate");

            Assert.That(handle.Status, Is.EqualTo(EPulletOperationStatus.Succeeded));
            Assert.That(handle.GameObj, Is.Not.Null);
            int completedCount = 0;
            handle.Completed += _ => completedCount++;
            Assert.That(completedCount, Is.EqualTo(1));
            handle.Restore();
        }

        [Test]
        public void CachedSpawnRestoreBenchmark()
        {
            SpawnHandle warmup = _spawner.SpawnAsync("Test/Benchmark");
            warmup.Restore();

            int[] batchSizes = { 100, 1000, 5000 };
            for (int batchIndex = 0; batchIndex < batchSizes.Length; batchIndex++)
            {
                int count = batchSizes[batchIndex];
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    SpawnHandle handle = _spawner.SpawnAsync("Test/Benchmark");
                    Assert.That(handle.IsDone, Is.True);
                    handle.Restore();
                }
                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                TestContext.WriteLine(
                    $"PulletPooling cached Spawn/Restore: count={count}, " +
                    $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}, allocatedBytes={allocated}");
            }

            Assert.That(_package.InstanceCount, Is.EqualTo(1));
        }

        [Test]
        public void ActiveInstanceScanBenchmark()
        {
            int[] activeCounts = { 100, 1000, 5000 };
            const int updateCount = 100;
            _package.InstanceDelayFrames = 1000000;

            for (int batchIndex = 0; batchIndex < activeCounts.Length; batchIndex++)
            {
                int activeCount = activeCounts[batchIndex];
                string location = "Test/ActiveScan/" + activeCount;
                var handles = new List<SpawnHandle>(activeCount);
                for (int i = 0; i < activeCount; i++)
                    handles.Add(_spawner.SpawnAsync(location));

                var stopwatch = Stopwatch.StartNew();
                for (int updateIndex = 0; updateIndex < updateCount; updateIndex++)
                    _spawner.Update();
                stopwatch.Stop();
                TestContext.WriteLine(
                    $"PulletPooling active scan: active={activeCount}, updates={updateCount}, " +
                    $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");

                for (int i = 0; i < handles.Count; i++)
                    handles[i].Discard();
                PulletOperationSystem.Update();
                _spawner.DestroyGameObjectPool(location);
            }
        }

        [UnityTest]
        public IEnumerator FailedPoolCreationCanBeRetried()
        {
            _package.EnqueueAssetResult(false);
            CreatePoolOperation failed = _spawner.CreateGameObjectPoolAsync("Test/Retry");
            while (!failed.IsDone)
                yield return null;
            Assert.That(failed.Status, Is.EqualTo(EPulletOperationStatus.Failed));

            _package.EnqueueAssetResult(true);
            CreatePoolOperation retry = _spawner.CreateGameObjectPoolAsync("Test/Retry");
            while (!retry.IsDone)
                yield return null;

            Assert.That(retry.Status, Is.EqualTo(EPulletOperationStatus.Succeeded));
            Assert.That(_package.AssetReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExternallyDestroyedInstanceIsNotCachedAgain()
        {
            SpawnHandle first = _spawner.SpawnAsync("Test/ExternalDestroy");
            while (!first.IsDone)
                yield return null;
            Assert.That(first.Status, Is.EqualTo(EPulletOperationStatus.Succeeded));
            GameObject instance = first.GameObj;

            UnityEngine.Object.Destroy(instance);
            yield return null;
            yield return null;
            first.Restore();

            SpawnHandle second = _spawner.SpawnAsync("Test/ExternalDestroy");
            while (!second.IsDone)
                yield return null;
            Assert.That(second.Status, Is.EqualTo(EPulletOperationStatus.Succeeded));
            Assert.That(_package.InstanceCount, Is.EqualTo(2));
            second.Restore();
        }

        [UnityTest]
        public IEnumerator DestroyPoolDuringSpawnFailsHandleAndReleasesAsset()
        {
            _package.InstanceDelayFrames = 4;
            SpawnHandle handle = _spawner.SpawnAsync("Test/DestroyDuringSpawn");
            int completedCount = 0;
            handle.Completed += _ => completedCount++;

            _spawner.DestroyGameObjectPool("Test/DestroyDuringSpawn");
            while (!handle.IsDone)
                yield return null;

            Assert.That(handle.Status, Is.EqualTo(EPulletOperationStatus.Failed));
            Assert.That(completedCount, Is.EqualTo(1));
            Assert.That(_package.AssetReleaseCount, Is.EqualTo(1));
            Assert.That(_package.LastInstance.CancelCount, Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator NullInstanceHandleDoesNotPoisonPool()
        {
            _package.ReturnNullInstanceOnce = true;
            Assert.Throws<InvalidOperationException>(() =>
                _spawner.SpawnAsync("Test/NullInstance"));

            SpawnHandle retry = _spawner.SpawnAsync("Test/NullInstance");
            while (!retry.IsDone)
                yield return null;

            Assert.That(retry.Status, Is.EqualTo(EPulletOperationStatus.Succeeded));
            retry.Restore();
        }

        private sealed class TestAdapter : IResourceAdapter
        {
            private readonly IResourcePackage _package;
            public TestAdapter(IResourcePackage package) { _package = package; }
            public string Name => "PoolingTests";
            public string DefaultPackageName => _package.Name;
            public bool TryGetPackage(string packageName, out IResourcePackage package)
            {
                package = packageName == _package.Name ? _package : null;
                return package != null;
            }
            public IResourcePackage GetPackage(string packageName) =>
                packageName == _package.Name ? _package : null;
        }

        private sealed class TestPackage : IResourcePackage, IDisposable
        {
            private readonly Queue<bool> _assetResults = new Queue<bool>();
            private readonly List<GameObject> _objects = new List<GameObject>();
            private readonly GameObject _prefab;

            public string Name => "DefaultPackage";
            public EResourcePackageStatus Status => EResourcePackageStatus.Succeeded;
            public string Error => null;
            public int InstanceDelayFrames { get; set; }
            public int InstanceCount { get; private set; }
            public int AssetReleaseCount { get; private set; }
            public bool ReturnNullInstanceOnce { get; set; }
            public TestInstanceHandle LastInstance { get; private set; }

            public TestPackage()
            {
                _prefab = new GameObject("PoolingTestPrefab");
                _prefab.SetActive(false);
                _objects.Add(_prefab);
            }

            public void EnqueueAssetResult(bool succeeded) => _assetResults.Enqueue(succeeded);
            public bool IsLocationValid(string location) => true;

            public IResourceAssetHandle LoadAssetAsync<TObject>(string location)
                where TObject : UnityEngine.Object
            {
                bool succeeded = _assetResults.Count == 0 || _assetResults.Dequeue();
                return new TestAssetHandle(this, succeeded, _prefab);
            }

            public TestInstanceHandle CreateInstance(GameObject prefab)
            {
                if (ReturnNullInstanceOnce)
                {
                    ReturnNullInstanceOnce = false;
                    return null;
                }
                InstanceCount++;
                LastInstance = new TestInstanceHandle(this, prefab, InstanceDelayFrames);
                return LastInstance;
            }

            public void Track(GameObject instance) => _objects.Add(instance);
            public void CountRelease() => AssetReleaseCount++;

            public void Dispose()
            {
                for (int i = 0; i < _objects.Count; i++)
                {
                    if (_objects[i] != null)
                        UnityEngine.Object.Destroy(_objects[i]);
                }
                _objects.Clear();
            }
        }

        private sealed class TestAssetHandle : IResourceAssetHandle
        {
            private readonly TestPackage _package;
            private readonly bool _succeeded;
            private readonly GameObject _prefab;
            private bool _released;

            public TestAssetHandle(TestPackage package, bool succeeded, GameObject prefab)
            {
                _package = package;
                _succeeded = succeeded;
                _prefab = prefab;
            }

            public bool IsValid => !_released;
            public bool IsDone => true;
            public bool IsSucceeded => !_released && _succeeded;
            public string Error => _succeeded ? null : "Asset load failed.";
            public UnityEngine.Object AssetObject => IsSucceeded ? _prefab : null;
            public object Current => null;
            public event Action<IResourceAssetHandle> Completed { add { } remove { } }
            public bool MoveNext() => false;
            public void Reset() { }
            public void WaitForCompletion() { }
            public GameObject InstantiateSync(ResourceInstantiateOptions options) => null;
            public IResourceInstanceHandle InstantiateAsync(ResourceInstantiateOptions options) =>
                _package.CreateInstance(_prefab);
            public void Release()
            {
                if (_released) return;
                _released = true;
                _package.CountRelease();
            }
        }

        private sealed class TestInstanceHandle : IResourceInstanceHandle
        {
            private readonly TestPackage _package;
            private readonly GameObject _prefab;
            private readonly int _completeFrame;
            private bool _cancelled;
            private bool _created;
            private GameObject _result;

            public int CancelCount { get; private set; }
            public bool IsDone => _cancelled || Time.frameCount >= _completeFrame;
            public bool IsSucceeded => IsDone && !_cancelled;
            public string Error => _cancelled ? "Instance cancelled." : null;
            public GameObject Result
            {
                get
                {
                    if (IsSucceeded && !_created)
                    {
                        _created = true;
                        _result = UnityEngine.Object.Instantiate(_prefab);
                        _package.Track(_result);
                    }
                    return _result;
                }
            }

            public TestInstanceHandle(TestPackage package, GameObject prefab, int delayFrames)
            {
                _package = package;
                _prefab = prefab;
                _completeFrame = Time.frameCount + delayFrames;
            }

            public void WaitForCompletion() { }
            public void Cancel()
            {
                CancelCount++;
                _cancelled = true;
            }
        }
    }
}
