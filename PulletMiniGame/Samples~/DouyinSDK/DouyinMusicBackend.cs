using System;
using PulletFramework.Sound;
using TTSDK;

namespace PulletMiniGame.Platform.Douyin
{
    /// <summary>使用抖音原生音频上下文播放远端背景音乐。</summary>
    public sealed class DouyinMusicBackend : IPulletMusicBackend
    {
        private TTAudioManager.InnerAudioContext _context;
        private bool _isPlaying;
        private bool _canResume;

        public bool IsPlaying => _isPlaying;
        public bool CanPlay(string location) => IsRemoteAudioUrl(location);

        public void Play(string location, bool loop, float volume)
        {
            StopAndDestroy();
            _context = TTAudioManager.CreateInnerAudioContext(new TTAudioManager.InnerAudioContextOptions
            {
                src = location,
                autoPlay = false,
                loop = loop,
                volume = Clamp01(volume),
                playbackRate = 1f,
                needDownload = false
            });
            if (_context == null)
                throw new InvalidOperationException("TTSDK failed to create an audio context.");
            _context.Play();
            _isPlaying = true;
            _canResume = true;
        }

        public void Stop()
        {
            if (_context != null) _context.Stop();
            _isPlaying = false;
            _canResume = false;
        }

        public void Pause()
        {
            if (_context == null || !_isPlaying) return;
            _context.Pause();
            _isPlaying = false;
        }

        public void Resume()
        {
            if (_context == null || _isPlaying || !_canResume) return;
            _context.Play();
            _isPlaying = true;
        }

        public void SetVolume(float volume)
        {
            if (_context != null) _context.Volume = Clamp01(volume);
        }

        public void Dispose() => StopAndDestroy();

        private void StopAndDestroy()
        {
            if (_context == null) return;
            _context.Stop();
            _context.Destroy();
            _context = null;
            _isPlaying = false;
            _canResume = false;
        }

        private static bool IsRemoteAudioUrl(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;
            string extension = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            return extension == ".mp3" || extension == ".wav"
                || extension == ".m4a" || extension == ".aac";
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
