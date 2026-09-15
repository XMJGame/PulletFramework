using System;
using System.Collections;
using NUnit.Framework;
using PulletFramework.Resource;
using PulletFramework.Sound;
using UnityEngine;
using UnityEngine.TestTools;

namespace PulletFramework.Tests
{
    public sealed class PulletSoundTests
    {
        private sealed class DelayedAssetHandle : IResourceAssetHandle
        {
            private readonly UnityEngine.Object _asset;
            private int _remainingFrames;

            public DelayedAssetHandle(UnityEngine.Object asset, int delayFrames = 2)
            {
                _asset = asset;
                _remainingFrames = delayFrames;
            }

            public bool IsValid => true;
            public bool IsDone { get; private set; }
            public bool IsSucceeded => IsDone;
            public string Error => null;
            public UnityEngine.Object AssetObject => IsDone ? _asset : null;
            public object Current => null;
            public event Action<IResourceAssetHandle> Completed;

            public bool MoveNext()
            {
                if (IsDone)
                    return false;
                if (_remainingFrames-- > 0)
                    return true;
                IsDone = true;
                Completed?.Invoke(this);
                return false;
            }

            public void Reset() { }
            public void WaitForCompletion()
            {
                while (MoveNext()) { }
            }
            public GameObject InstantiateSync(ResourceInstantiateOptions options) => null;
            public IResourceInstanceHandle InstantiateAsync(ResourceInstantiateOptions options) => null;
            public void Release() { }
        }

        private sealed class TestPackage : IResourcePackage
        {
            private readonly AudioClip _clip;

            public TestPackage(AudioClip clip)
            {
                _clip = clip;
            }

            public string Name => "DefaultPackage";
            public EResourcePackageStatus Status => EResourcePackageStatus.Succeeded;
            public string Error => null;
            public bool IsLocationValid(string location) => true;
            public IResourceAssetHandle LoadAssetAsync<TObject>(string location)
                where TObject : UnityEngine.Object
            {
                return new DelayedAssetHandle(_clip);
            }
        }

        private sealed class TestAdapter : IResourceAdapter
        {
            private readonly IResourcePackage _package;

            public TestAdapter(AudioClip clip)
            {
                _package = new TestPackage(clip);
            }

            public string Name => "SoundTests";
            public string DefaultPackageName => "DefaultPackage";
            public bool TryGetPackage(string packageName, out IResourcePackage package)
            {
                package = _package;
                return true;
            }
            public IResourcePackage GetPackage(string packageName) => _package;
        }

        private sealed class TestMusicBackend : IPulletMusicBackend
        {
            public bool IsPlaying { get; private set; }
            public int PauseCount { get; private set; }
            public int ResumeCount { get; private set; }

            public bool CanPlay(string location) => true;
            public void Play(string location, bool loop, float volume) => IsPlaying = true;
            public void Stop() => IsPlaying = false;
            public void Pause()
            {
                PauseCount++;
                IsPlaying = false;
            }
            public void Resume()
            {
                ResumeCount++;
                IsPlaying = true;
            }
            public void SetVolume(float volume) { }
            public void Dispose() { }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PulletFrameworks.Destroy();
            PulletResources.Uninstall();
            yield return null;
            PulletFrameworks.Initialize();
            PulletSound.Initialize(new PulletSoundOptions
            {
                PersistPreferences = false,
                DefaultMusicFadeSeconds = 0f
            });
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PulletFrameworks.Destroy();
            PulletResources.Uninstall();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DirectMusicInvalidatesOlderLocationRequest()
        {
            AudioClip delayed = AudioClip.Create("DelayedMusic", 256, 1, 8000, false);
            AudioClip direct = AudioClip.Create("DirectMusic", 256, 1, 8000, false);
            PulletResources.Install(new TestAdapter(delayed));

            PulletSound.PlayMusic("music/delayed");
            PulletSound.PlayMusic(direct, fadeSeconds: 0f);
            yield return null;
            yield return null;
            yield return null;
            yield return null;

            Assert.That(PulletSound.CurrentMusicClip, Is.SameAs(direct));
            UnityEngine.Object.Destroy(delayed);
            UnityEngine.Object.Destroy(direct);
        }

        [UnityTest]
        public IEnumerator DirectVoiceInvalidatesOlderLocationRequest()
        {
            AudioClip delayed = AudioClip.Create("DelayedVoice", 256, 1, 8000, false);
            AudioClip direct = AudioClip.Create("DirectVoice", 256, 1, 8000, false);
            PulletResources.Install(new TestAdapter(delayed));

            PulletSound.PlayVoice("voice/delayed");
            PulletSound.PlayVoice(direct);
            yield return null;
            yield return null;
            yield return null;
            yield return null;

            Assert.That(PulletSound.CurrentVoiceClip, Is.SameAs(direct));
            UnityEngine.Object.Destroy(delayed);
            UnityEngine.Object.Destroy(direct);
        }

        [Test]
        public void PauseReasonsMustAllBeClearedBeforeMusicResumes()
        {
            var backend = new TestMusicBackend();
            PulletSound.SetMusicBackend(backend);
            PulletSound.PlayMusic("https://example.invalid/music.mp3");

            PulletSound.PauseMusic();
            PulletSound.PauseAll();
            PulletSound.ResumeMusic();

            Assert.That(PulletSound.IsPaused(ESoundChannel.Music), Is.True);
            Assert.That(backend.ResumeCount, Is.Zero);

            PulletSound.ResumeAll();
            Assert.That(PulletSound.IsPaused(ESoundChannel.Music), Is.False);
            Assert.That(backend.ResumeCount, Is.EqualTo(1));

            PulletSound.SetApplicationPaused(true);
            PulletSound.PauseMusic();
            PulletSound.SetApplicationPaused(false);
            Assert.That(PulletSound.IsPaused(ESoundChannel.Music), Is.True);
            Assert.That(backend.ResumeCount, Is.EqualTo(1));

            PulletSound.ResumeMusic();
            Assert.That(PulletSound.IsPaused(ESoundChannel.Music), Is.False);
            Assert.That(backend.ResumeCount, Is.EqualTo(2));
        }
    }
}
