using System;

namespace PulletFramework.Sound
{
    /// <summary>
    /// 长音频播放后端。小游戏平台可接管远端背景音乐，短音效仍由 Unity AudioSource 播放。
    /// </summary>
    public interface IPulletMusicBackend : IDisposable
    {
        bool IsPlaying { get; }

        /// <summary>判断该后端是否可以直接播放指定地址。</summary>
        bool CanPlay(string location);

        void Play(string location, bool loop, float volume);
        void Stop();
        void Pause();
        void Resume();
        void SetVolume(float volume);
    }
}
