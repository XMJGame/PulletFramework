using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PulletFramework.Setting;
using PulletFramework.MiniGame.Platform;
using UnityEngine.TestTools;

namespace PulletFramework.MiniGame.Tests
{
    public sealed class PulletMiniGamesInitializationTests
    {
        [SetUp]
        public void SetUp()
        {
            PulletMiniGames.Shutdown();
            PulletPlayerPrefs.UninstallBackend(false);
        }

        [TearDown]
        public void TearDown()
        {
            PulletMiniGames.Shutdown();
            PulletPlayerPrefs.UninstallBackend(false);
        }

        [UnityTest]
        public IEnumerator ConcurrentInitializationSharesRequestAndInstallsStorageOnce()
        {
            var adapter = new ControlledAdapter("shared");

            Task<PlatformResult> first = PulletMiniGames.InitializeAsync(adapter);
            Task<PlatformResult> second = PulletMiniGames.InitializeAsync(adapter);
            Assert.That(adapter.InitializeCount, Is.EqualTo(1));

            adapter.Complete(0, PlatformResult.Success());
            while (!first.IsCompleted || !second.IsCompleted)
                yield return null;

            Assert.That(first.GetAwaiter().GetResult().Succeeded, Is.True);
            Assert.That(second.GetAwaiter().GetResult().Succeeded, Is.True);
            Assert.That(PulletPlayerPrefs.IsUsingCustomBackend, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedInitializationCanBeRetried()
        {
            var adapter = new ControlledAdapter("retry");
            Task<PlatformResult> first = PulletMiniGames.InitializeAsync(adapter);
            adapter.Complete(0, PlatformResult.Failure("first failed"));
            while (!first.IsCompleted)
                yield return null;
            Assert.That(first.GetAwaiter().GetResult().Succeeded, Is.False);

            Task<PlatformResult> retry = PulletMiniGames.InitializeAsync(adapter);
            Assert.That(adapter.InitializeCount, Is.EqualTo(2));
            adapter.Complete(1, PlatformResult.Success());
            while (!retry.IsCompleted)
                yield return null;

            Assert.That(retry.GetAwaiter().GetResult().Succeeded, Is.True);
        }

        [UnityTest]
        public IEnumerator SupersededInitializationCannotReplaceCurrentStorage()
        {
            var oldAdapter = new ControlledAdapter("old");
            var currentAdapter = new ControlledAdapter("current");
            Task<PlatformResult> oldTask = PulletMiniGames.InitializeAsync(oldAdapter);
            Task<PlatformResult> currentTask = PulletMiniGames.InitializeAsync(currentAdapter);

            currentAdapter.Complete(0, PlatformResult.Success());
            while (!currentTask.IsCompleted)
                yield return null;
            Assert.That(currentTask.GetAwaiter().GetResult().Succeeded, Is.True);
            oldAdapter.Complete(0, PlatformResult.Success());
            while (!oldTask.IsCompleted)
                yield return null;

            PlatformResult oldResult = oldTask.GetAwaiter().GetResult();
            Assert.That(oldResult.Succeeded, Is.False);
            PulletPlayerPrefs.SetString("owner", "current");
            Assert.That(currentAdapter.Storage.GetString("owner"), Is.EqualTo("current"));
            Assert.That(oldAdapter.Storage.HasKey("owner"), Is.False);
        }

        [UnityTest]
        public IEnumerator ShutdownRejectsLateInitializationResult()
        {
            var adapter = new ControlledAdapter("late");
            Task<PlatformResult> initialization = PulletMiniGames.InitializeAsync(adapter);

            PulletMiniGames.Shutdown();
            adapter.Complete(0, PlatformResult.Success());
            while (!initialization.IsCompleted)
                yield return null;

            Assert.That(initialization.GetAwaiter().GetResult().Succeeded, Is.False);
            Assert.That(PulletPlayerPrefs.IsUsingCustomBackend, Is.False);
            Assert.That(PulletMiniGames.IsInstalled, Is.False);
        }

        [UnityTest]
        public IEnumerator CancellingOneWaiterDoesNotCancelSharedInitialization()
        {
            var adapter = new ControlledAdapter("cancel");
            var cancellation = new CancellationTokenSource();
            Task<PlatformResult> cancelled = PulletMiniGames.InitializeAsync(adapter, cancellation.Token);
            Task<PlatformResult> active = PulletMiniGames.InitializeAsync(adapter);

            cancellation.Cancel();
            while (!cancelled.IsCompleted)
                yield return null;
            Assert.Throws<OperationCanceledException>(() => cancelled.GetAwaiter().GetResult());
            adapter.Complete(0, PlatformResult.Success());
            while (!active.IsCompleted)
                yield return null;

            Assert.That(active.GetAwaiter().GetResult().Succeeded, Is.True);
            Assert.That(adapter.InitializeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ShutdownDoesNotRemoveBackendOwnedByAnotherModule()
        {
            var adapter = new ControlledAdapter("storage-owner");
            Task<PlatformResult> initialization = PulletMiniGames.InitializeAsync(adapter);
            adapter.Complete(0, PlatformResult.Success());
            while (!initialization.IsCompleted)
                yield return null;
            Assert.That(initialization.GetAwaiter().GetResult().Succeeded, Is.True);

            var replacement = new MemoryBackend();
            PulletPlayerPrefs.InstallBackend(replacement);
            PulletMiniGames.Shutdown();

            Assert.That(PulletPlayerPrefs.IsBackend(replacement), Is.True);
        }

        private sealed class ControlledAdapter : IPlatformAdapter
        {
            private readonly List<TaskCompletionSource<PlatformResult>> _requests =
                new List<TaskCompletionSource<PlatformResult>>();

            public string Id { get; }
            public EPlatformCapability Capabilities => EPlatformCapability.Storage;
            public bool IsInitialized { get; private set; }
            public int InitializeCount => _requests.Count;
            public MemoryStorage Storage { get; } = new MemoryStorage();

            public ControlledAdapter(string id)
            {
                Id = id;
            }

            public Task<PlatformResult> InitializeAsync(CancellationToken cancellationToken = default)
            {
                var source = new TaskCompletionSource<PlatformResult>();
                _requests.Add(source);
                return source.Task;
            }

            public void Complete(int index, PlatformResult result)
            {
                IsInitialized = result.Succeeded;
                _requests[index].TrySetResult(result);
            }

            public void Update(float deltaTime, float unscaledDeltaTime) { }
            public void Shutdown() => IsInitialized = false;

            public bool TryGetService(Type serviceType, out object service)
            {
                service = serviceType == typeof(IPlatformStorageService) ? Storage : null;
                return service != null;
            }
        }

        private sealed class MemoryStorage : IPlatformStorageService
        {
            private readonly Dictionary<string, string> _values =
                new Dictionary<string, string>();

            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue = "") =>
                _values.TryGetValue(key, out string value) ? value : defaultValue;
            public void SetString(string key, string value) => _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }

        private sealed class MemoryBackend : IPulletPlayerPrefsBackend
        {
            private readonly MemoryStorage _storage = new MemoryStorage();
            public bool HasKey(string key) => _storage.HasKey(key);
            public int GetInt(string key, int defaultValue) => defaultValue;
            public float GetFloat(string key, float defaultValue) => defaultValue;
            public string GetString(string key, string defaultValue) =>
                _storage.GetString(key, defaultValue);
            public void SetInt(string key, int value) => _storage.SetString(key, value.ToString());
            public void SetFloat(string key, float value) => _storage.SetString(key, value.ToString());
            public void SetString(string key, string value) => _storage.SetString(key, value);
            public void DeleteKey(string key) => _storage.DeleteKey(key);
            public void Save() { }
        }
    }
}
