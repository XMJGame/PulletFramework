using System;
using System.Collections;
using System.Collections.Generic;
using PulletFramework.Resource;
using PulletFramework.Setting;
using UnityEngine;
using UnityEngine.Audio;

namespace PulletFramework.Sound
{
    /// <summary>可独立控制音量、静音和暂停的音频分类。</summary>
    public enum ESoundChannel { Music, SoundEffect, Voice }

    /// <summary>声音系统启动参数。</summary>
    public sealed class PulletSoundOptions
    {
        public int SoundEffectSourceCount = 8;
        public bool PersistPreferences = true;
        public string PreferenceKeyPrefix = "Pullet.Sound";
        public float DefaultMusicFadeSeconds = 0.25f;
        public AudioMixerGroup MusicOutput;
        public AudioMixerGroup SoundEffectOutput;
        public AudioMixerGroup VoiceOutput;
    }

    /// <summary>通用声音服务，管理音乐、并发音效、语音、资源句柄和用户设置。</summary>
    public static class PulletSound
    {
        private sealed class CachedClip
        {
            public AudioClip Clip;
            public IResourceAssetHandle Handle;
        }

        private sealed class EffectSlot
        {
            public AudioSource Source;
            public float LocalVolume = 1f;
            public float StartedAt;
        }

        private static readonly Dictionary<string, CachedClip> Clips =
            new Dictionary<string, CachedClip>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<Action<AudioClip>>> PendingLoads =
            new Dictionary<string, List<Action<AudioClip>>>(StringComparer.Ordinal);
        private static readonly List<EffectSlot> EffectSlots = new List<EffectSlot>();
        private static readonly float[] ChannelVolumes = { 1f, 1f, 1f };
        private static readonly bool[] ChannelMutes = new bool[3];

        private static bool _initialized;
        private static bool _destroying;
        private static bool _masterMuted;
        private static float _masterVolume = 1f;
        private static GameObject _root;
        private static AudioSource[] _musicSources;
        private static AudioSource _voiceSource;
        private static float _voiceLocalVolume = 1f;
        private static int _activeMusic;
        private static int _musicRequest;
        private static int _soundEffectRequest;
        private static int _voiceRequest;
        private static int _lifetime;
        private static bool _fading;
        private static float _fadeElapsed;
        private static float _fadeDuration;
        private static AudioSource _fadeFrom;
        private static AudioSource _fadeTo;
        private static PulletSoundOptions _options;
        private static bool _preferencesDirty;
        private static float _preferencesSaveAt;

        public static bool IsInitialized => _initialized;
        public static bool IsMasterMuted => _masterMuted;
        public static bool IsMusicPlaying => _initialized && ActiveMusic.isPlaying;
        public static float MasterVolume => _masterVolume;
        public static float MusicVolume => GetVolume(ESoundChannel.Music);
        public static float SoundEffectVolume => GetVolume(ESoundChannel.SoundEffect);
        public static float VoiceVolume => GetVolume(ESoundChannel.Voice);
        public static event Action SettingsChanged;

        private static AudioSource ActiveMusic => _musicSources[_activeMusic];

        /// <summary>显式初始化。未调用时，第一次播放会自动初始化。</summary>
        public static void Initialize(PulletSoundOptions options = null)
        {
            if (_initialized) return;
            _options = options ?? new PulletSoundOptions();
            _options.SoundEffectSourceCount = Mathf.Clamp(_options.SoundEffectSourceCount, 1, 32);
            if (string.IsNullOrWhiteSpace(_options.PreferenceKeyPrefix))
                _options.PreferenceKeyPrefix = "Pullet.Sound";
            _masterMuted = false;
            _masterVolume = 1f;
            for (int i = 0; i < ChannelVolumes.Length; i++)
            {
                ChannelVolumes[i] = 1f;
                ChannelMutes[i] = false;
            }

            _root = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletSound)}]");
            _musicSources = new[]
            {
                CreateSource("Music A", true, _options.MusicOutput),
                CreateSource("Music B", true, _options.MusicOutput)
            };
            _voiceSource = CreateSource("Voice", false, _options.VoiceOutput);
            for (int i = 0; i < _options.SoundEffectSourceCount; i++)
                EffectSlots.Add(new EffectSlot
                {
                    Source = CreateSource($"Sound Effect {i + 1}", false, _options.SoundEffectOutput)
                });
            _initialized = true;
            if (_options.PersistPreferences) LoadPreferences();
            ApplyVolumes();
            PLogger.Log($"{nameof(PulletSound)} initialized with {EffectSlots.Count} SFX sources.");
        }

        /// <summary>播放或平滑切换背景音乐。</summary>
        public static void PlayMusic(string location, bool loop = true, float fadeSeconds = -1f)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(location)) { StopMusic(fadeSeconds); return; }
            int request = ++_musicRequest;
            LoadClip(location, clip => { if (request == _musicRequest) PlayMusic(clip, loop, fadeSeconds); });
        }

        public static void PlayMusic(AudioClip clip, bool loop = true, float fadeSeconds = -1f)
        {
            if (clip == null) return;
            EnsureInitialized();
            AudioSource current = ActiveMusic;
            if (current.clip == clip && current.isPlaying) return;
            AudioSource next = _musicSources[1 - _activeMusic];
            next.Stop();
            next.clip = clip;
            next.loop = loop;
            next.volume = 0f;
            next.Play();
            _activeMusic = 1 - _activeMusic;
            BeginFade(current, next, ResolveFade(fadeSeconds));
        }

        public static void StopMusic(float fadeSeconds = -1f)
        {
            if (!_initialized) return;
            ++_musicRequest;
            BeginFade(ActiveMusic, null, ResolveFade(fadeSeconds));
        }

        public static void PauseMusic()
        {
            if (!_initialized) return;
            foreach (AudioSource source in _musicSources) source.Pause();
        }

        public static void ResumeMusic()
        {
            if (!_initialized) return;
            foreach (AudioSource source in _musicSources) source.UnPause();
        }

        /// <summary>播放可与其他音效重叠的短音效。</summary>
        public static void PlaySound(string location, float volume = 1f, float pitch = 1f)
        {
            EnsureInitialized();
            int request = _soundEffectRequest;
            LoadClip(location, clip =>
            {
                if (request == _soundEffectRequest)
                    PlaySound(clip, volume, pitch);
            });
        }

        public static void PlaySound(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            EnsureInitialized();
            EffectSlot slot = GetEffectSlot();
            slot.Source.Stop();
            slot.Source.clip = clip;
            slot.Source.loop = false;
            slot.Source.pitch = Mathf.Clamp(pitch, -3f, 3f);
            slot.LocalVolume = Mathf.Clamp01(volume);
            slot.StartedAt = Time.unscaledTime;
            ApplyEffectVolume(slot);
            slot.Source.Play();
        }

        /// <summary>播放语音或旁白，新语音会替换正在播放的语音。</summary>
        public static void PlayVoice(string location, bool loop = false, float volume = 1f)
        {
            EnsureInitialized();
            int request = ++_voiceRequest;
            LoadClip(location, clip => { if (request == _voiceRequest) PlayVoice(clip, loop, volume); });
        }

        public static void PlayVoice(AudioClip clip, bool loop = false, float volume = 1f)
        {
            if (clip == null) return;
            EnsureInitialized();
            _voiceSource.Stop();
            _voiceSource.clip = clip;
            _voiceSource.loop = loop;
            _voiceLocalVolume = Mathf.Clamp01(volume);
            ApplyVoiceVolume();
            _voiceSource.Play();
        }

        public static void StopVoice()
        {
            if (!_initialized) return;
            ++_voiceRequest;
            _voiceSource.Stop();
            _voiceSource.clip = null;
        }

        public static void PauseAll()
        {
            if (!_initialized) return;
            ForEachSource(source => source.Pause());
        }

        public static void ResumeAll()
        {
            if (!_initialized) return;
            ForEachSource(source => source.UnPause());
        }

        public static void Pause(ESoundChannel channel)
        {
            if (!_initialized) return;
            ForEachChannelSource(channel, source => source.Pause());
        }

        public static void Resume(ESoundChannel channel)
        {
            if (!_initialized) return;
            ForEachChannelSource(channel, source => source.UnPause());
        }

        public static void Stop(ESoundChannel channel, float musicFadeSeconds = 0f)
        {
            if (!_initialized) return;
            if (channel == ESoundChannel.Music)
            {
                StopMusic(musicFadeSeconds);
                return;
            }
            ForEachChannelSource(channel, source =>
            {
                source.Stop();
                source.clip = null;
            });
            if (channel == ESoundChannel.Voice) ++_voiceRequest;
            if (channel == ESoundChannel.SoundEffect) ++_soundEffectRequest;
        }

        public static float GetVolume(ESoundChannel channel) => ChannelVolumes[(int)channel];
        public static bool IsMuted(ESoundChannel channel) => ChannelMutes[(int)channel];

        public static void SetMasterVolume(float volume)
        {
            EnsureInitialized();
            _masterVolume = Mathf.Clamp01(volume);
            ApplyAndScheduleSave();
        }

        public static void SetVolume(ESoundChannel channel, float volume)
        {
            EnsureInitialized();
            ChannelVolumes[(int)channel] = Mathf.Clamp01(volume);
            ApplyAndScheduleSave();
        }

        public static void SetMasterMuted(bool muted)
        {
            EnsureInitialized();
            _masterMuted = muted;
            ApplyAndScheduleSave();
        }
        public static void ToggleMasterMuted() => SetMasterMuted(!_masterMuted);

        public static void SetMuted(ESoundChannel channel, bool muted)
        {
            EnsureInitialized();
            ChannelMutes[(int)channel] = muted;
            ApplyAndScheduleSave();
        }

        /// <summary>提前加载常用音频，减少首次播放等待。</summary>
        public static void Preload(string location, Action<bool> completed = null)
        {
            EnsureInitialized();
            LoadClip(location, clip => completed?.Invoke(clip != null));
        }

        /// <summary>停止播放并释放所有由资源系统加载的音频。</summary>
        public static void UnloadAll()
        {
            if (_initialized) StopSources();
            ++_lifetime;
            CompletePendingLoads(null);
            foreach (CachedClip cached in Clips.Values) cached.Handle?.Release();
            Clips.Clear();
        }

        public static void SavePreferences()
        {
            if (_options == null || !_options.PersistPreferences) return;
            string prefix = _options.PreferenceKeyPrefix;
            PulletPlayerPrefs.SetInt(prefix + ".MasterMuted", _masterMuted ? 1 : 0);
            PulletPlayerPrefs.SetFloat(prefix + ".MasterVolume", _masterVolume);
            for (int i = 0; i < ChannelVolumes.Length; i++)
            {
                PulletPlayerPrefs.SetInt(prefix + ".Muted." + i, ChannelMutes[i] ? 1 : 0);
                PulletPlayerPrefs.SetFloat(prefix + ".Volume." + i, ChannelVolumes[i]);
            }
            PulletPlayerPrefs.Save();
            _preferencesDirty = false;
        }

        public static void LoadPreferences()
        {
            if (_options == null || !_options.PersistPreferences) return;
            string prefix = _options.PreferenceKeyPrefix;
            _masterMuted = PulletPlayerPrefs.GetInt(prefix + ".MasterMuted", 0) != 0;
            _masterVolume = Mathf.Clamp01(PulletPlayerPrefs.GetFloat(prefix + ".MasterVolume", 1f));
            for (int i = 0; i < ChannelVolumes.Length; i++)
            {
                ChannelMutes[i] = PulletPlayerPrefs.GetInt(prefix + ".Muted." + i, 0) != 0;
                ChannelVolumes[i] = Mathf.Clamp01(PulletPlayerPrefs.GetFloat(prefix + ".Volume." + i, 1f));
            }
            ApplyVolumes();
        }

        public static void Destroy()
        {
            if (!_initialized)
                return;
            _destroying = true;
            if (_preferencesDirty) SavePreferences();
            ++_musicRequest;
            ++_soundEffectRequest;
            ++_voiceRequest;
            UnloadAll();
            EffectSlots.Clear();
            _musicSources = null;
            _voiceSource = null;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _options = null;
            _initialized = false;
            _fading = false;
            SettingsChanged = null;
            _destroying = false;
        }

        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (!_initialized) return;
            if (_preferencesDirty && Time.unscaledTime >= _preferencesSaveAt)
                SavePreferences();
            if (!_fading) return;
            _fadeElapsed += unscaledDeltaTime;
            float progress = _fadeDuration <= 0f ? 1f : Mathf.Clamp01(_fadeElapsed / _fadeDuration);
            if (_fadeFrom != null) _fadeFrom.volume = OutputVolume(ESoundChannel.Music) * (1f - progress);
            if (_fadeTo != null) _fadeTo.volume = OutputVolume(ESoundChannel.Music) * progress;
            if (progress < 1f) return;
            if (_fadeFrom != null) { _fadeFrom.Stop(); _fadeFrom.clip = null; }
            _fading = false;
            _fadeFrom = null;
            _fadeTo = null;
        }

        private static void LoadClip(string location, Action<AudioClip> completed)
        {
            if (_destroying) { completed?.Invoke(null); return; }
            if (string.IsNullOrWhiteSpace(location)) { completed?.Invoke(null); return; }
            if (Clips.TryGetValue(location, out CachedClip cached)) { completed?.Invoke(cached.Clip); return; }
            if (PendingLoads.TryGetValue(location, out List<Action<AudioClip>> callbacks))
            {
                callbacks.Add(completed);
                return;
            }
            PendingLoads.Add(location, new List<Action<AudioClip>> { completed });
            PulletFrameworks.StartCoroutine(LoadClipRoutine(location, _lifetime));
        }

        private static IEnumerator LoadClipRoutine(string location, int lifetime)
        {
            IResourceAssetHandle handle = null;
            AudioClip clip = null;
            try { handle = PulletResources.LoadAssetAsync<AudioClip>(location); }
            catch (Exception exception) { PLogger.Error($"加载音频失败：{location}\n{exception.Message}"); }
            if (handle != null)
            {
                yield return handle;
                if (handle.IsSucceeded) clip = handle.AssetObject as AudioClip;
            }
            if (lifetime != _lifetime || !_initialized) { handle?.Release(); yield break; }
            if (clip != null) Clips[location] = new CachedClip { Clip = clip, Handle = handle };
            else { PLogger.Error($"加载音频失败：{location}，{handle?.Error}"); handle?.Release(); }
            if (!PendingLoads.TryGetValue(location, out List<Action<AudioClip>> callbacks)) yield break;
            PendingLoads.Remove(location);
            for (int i = 0; i < callbacks.Count; i++)
                InvokeLoadCallback(callbacks[i], clip);
        }

        private static void CompletePendingLoads(AudioClip clip)
        {
            if (PendingLoads.Count == 0)
                return;
            var pending = new List<List<Action<AudioClip>>>(PendingLoads.Values);
            PendingLoads.Clear();
            for (int i = 0; i < pending.Count; i++)
            {
                List<Action<AudioClip>> callbacks = pending[i];
                for (int j = 0; j < callbacks.Count; j++)
                    InvokeLoadCallback(callbacks[j], clip);
            }
        }

        private static void InvokeLoadCallback(Action<AudioClip> callback, AudioClip clip)
        {
            if (callback == null)
                return;
            try
            {
                callback(clip);
            }
            catch (Exception exception)
            {
                PLogger.Error($"音频加载回调执行失败：{exception.Message}");
            }
        }

        private static void EnsureInitialized() { if (!_initialized) Initialize(); }

        private static AudioSource CreateSource(string name, bool loop, AudioMixerGroup output)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = output;
            return source;
        }

        private static EffectSlot GetEffectSlot()
        {
            EffectSlot oldest = EffectSlots[0];
            foreach (EffectSlot slot in EffectSlots)
            {
                if (!slot.Source.isPlaying) return slot;
                if (slot.StartedAt < oldest.StartedAt) oldest = slot;
            }
            return oldest;
        }

        private static void BeginFade(AudioSource from, AudioSource to, float duration)
        {
            _fadeFrom = from != null && from.isPlaying ? from : null;
            _fadeTo = to;
            _fadeElapsed = 0f;
            _fadeDuration = Mathf.Max(0f, duration);
            _fading = true;
            if (_fadeDuration <= 0f) Update(0f, 1f);
        }

        private static float ResolveFade(float seconds) => seconds < 0f ? _options.DefaultMusicFadeSeconds : seconds;
        private static float OutputVolume(ESoundChannel channel) =>
            _masterMuted || ChannelMutes[(int)channel] ? 0f : _masterVolume * ChannelVolumes[(int)channel];

        private static void ApplyAndScheduleSave()
        {
            ApplyVolumes();
            if (_options != null && _options.PersistPreferences)
            {
                _preferencesDirty = true;
                _preferencesSaveAt = Time.unscaledTime + 0.5f;
            }
            SettingsChanged?.Invoke();
        }

        private static void ApplyVolumes()
        {
            if (!_initialized) return;
            foreach (AudioSource source in _musicSources) source.volume = OutputVolume(ESoundChannel.Music);
            ApplyVoiceVolume();
            foreach (EffectSlot slot in EffectSlots) ApplyEffectVolume(slot);
        }

        private static void ApplyVoiceVolume() =>
            _voiceSource.volume = OutputVolume(ESoundChannel.Voice) * _voiceLocalVolume;
        private static void ApplyEffectVolume(EffectSlot slot) =>
            slot.Source.volume = OutputVolume(ESoundChannel.SoundEffect) * slot.LocalVolume;

        private static void ForEachSource(Action<AudioSource> action)
        {
            foreach (AudioSource source in _musicSources) action(source);
            action(_voiceSource);
            foreach (EffectSlot slot in EffectSlots) action(slot.Source);
        }

        private static void ForEachChannelSource(ESoundChannel channel, Action<AudioSource> action)
        {
            switch (channel)
            {
                case ESoundChannel.Music:
                    foreach (AudioSource source in _musicSources) action(source);
                    break;
                case ESoundChannel.SoundEffect:
                    foreach (EffectSlot slot in EffectSlots) action(slot.Source);
                    break;
                case ESoundChannel.Voice:
                    action(_voiceSource);
                    break;
            }
        }

        private static void StopSources()
        {
            ForEachSource(source => { source.Stop(); source.clip = null; });
        }

        [Obsolete("Use PlayMusic instead.")]
        public static void PlayBGM(string path, bool isLoop = true) => PlayMusic(path, isLoop);
        [Obsolete("Use PlayVoice instead.")]
        public static void PlayBroadcastSound(string path, bool isLoop = false) => PlayVoice(path, isLoop);
        [Obsolete("Use PlaySound instead.")]
        public static void PlayButSound(string path, float volume = 1f, bool isLoop = false) => PlaySound(path, volume);
        [Obsolete("Use PlaySound instead.")]
        public static void PlayButSound(AudioClip clip, float volume = 1f) => PlaySound(clip, volume);
    }
}
