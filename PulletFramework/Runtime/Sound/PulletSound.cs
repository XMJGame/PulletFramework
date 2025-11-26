using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace PulletFramework.Sound
{
    public static class PulletSound
    {
        private static bool m_IsInitialize = false;

        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }
        //背景音乐 -- 播放器
        private static AudioSource m_BGMSource;
        //播报 播放器
        private static AudioSource m_BroadcastSource;
        public static AudioSource dubbingSource { get { return m_BroadcastSource; } }
        //按钮音效 -- 播放器
        private static AudioSource m_BtnSource;

        private static AudioMixer m_AudioMixer;
        //音乐剪辑片断字典
        private static Dictionary<string, AudioClip> _AudioClipList = new Dictionary<string, AudioClip>();

        /// <summary>
        /// 背景音量
        /// </summary>
        private static float _BgmVolume = 1;
        public static float bgmVolume 
        {
            get { return _BgmVolume; }
            set { _BgmVolume = value; m_BGMSource.volume = _BgmVolume; }
        }

        /// <summary>
        /// 播报音量
        /// </summary>
        private static float m_BroadcastVolume = 1;
        public static float broadcastVolume 
        {
            get { return m_BroadcastVolume; }
            set { m_BroadcastVolume = value; m_BroadcastSource.volume = m_BroadcastVolume; }
        }
        /// <summary>
        /// 初始化声音系统
        /// </summary>
        /// <param name="createParam"></param>
        public static void Initalize()
        {
            if (m_IsInitialize)
                throw new Exception($"{nameof(PulletSound)} is initialized !");

            if (m_IsInitialize == false)
            {
                // 创建驱动器
                m_IsInitialize = true;
                m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletSound)}]");
                m_BGMSource = gameObject.AddComponent<AudioSource>();
                m_BroadcastSource = gameObject.AddComponent<AudioSource>();
                m_BtnSource = gameObject.AddComponent<AudioSource>();

                m_BGMSource.playOnAwake = false;
                m_BroadcastSource.playOnAwake = false;
                m_BtnSource.playOnAwake = false;

                PLogger.Log($"{nameof(PulletSound)} initalize !");
            }
        }


        /// <summary>
        /// 销毁声音系统
        /// </summary>
        public static void Destroy()
        {
            _AudioClipList.Clear();
        }

        /// <summary>
        /// 更新销毁系统
        /// </summary>
        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
  
        }

        #region 背景声音相关
        private static string _CurBgmPath = "";
        public static void PlayBGM(string path, bool isLoop = true)
        {
            if (path == "")
            {
                _CurBgmPath = path;
                m_BGMSource.clip = null;
                return;
            }
            if (_CurBgmPath == path)
            {
                return;
            }
            _CurBgmPath = path;
            if (_AudioClipList.ContainsKey(path))
            {
                PlayBGM(_AudioClipList[path], isLoop);
            }
            else
            {
                //异步加载资源
                PulletFrameworks.StartCoroutine(PrepareAudioClip(path, delegate (AudioClip audioClip)
                {
                    PlayBGM(audioClip, isLoop);
                }));
            }
        }

        private static void PlayBGM(AudioClip audioClip, bool isLoop = true)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            m_BGMSource.clip = audioClip;
            m_BGMSource.volume = _BgmVolume;
            m_BGMSource.Play();
            m_BGMSource.mute = false;
            m_BGMSource.loop = isLoop;
        }
        #endregion

        #region 播放 文字配音
        /// <summary>
        /// 播放文字配音
        /// </summary>
        /// <param name="path"></param>
        /// <param name="volume"></param>
        /// <param name="isLoop"></param>
        public static void PlayBroadcastSound(string path, bool isLoop = false)
        {
            if (_AudioClipList.ContainsKey(path))
            {
                AudioClip audioClip = _AudioClipList[path];
                PlayBroadcastSound(audioClip, isLoop);
            }
            else
            {
                //异步加载资源
                PulletFrameworks.StartCoroutine(PrepareAudioClip(path, delegate (AudioClip audioClip)
                {
                    PlayBroadcastSound(audioClip, isLoop);
                }));
            }
        }

        private static void PlayBroadcastSound(AudioClip audioClip, bool isLoop = true)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            m_BroadcastSource.clip = audioClip;
            m_BroadcastSource.volume = m_BroadcastVolume;
            m_BroadcastSource.Play();
            m_BroadcastSource.mute = false;
            m_BroadcastSource.loop = isLoop;
        }
        #endregion

        #region 播放 UI 声音

        /// <summary>
        /// 播放按钮声音
        /// </summary>
        /// <param name="audioClip"></param>
        /// <param name="volume"></param>
        public static void PlayButSound(AudioClip audioClip, float volume = 1)
        {
            PlayButSound(audioClip, volume,false);
        }
        /// <summary>
        /// 播放按钮声音
        /// </summary>
        /// <param name="path"></param>
        /// <param name="volume"></param>
        /// <param name="isLoop"></param>
        public static void PlayButSound(string path, float volume = 1, bool isLoop = false)
        {
            if (_AudioClipList.ContainsKey(path))
            {
                PlayButSound(_AudioClipList[path], volume, isLoop);
            }
            else
            {
                //异步加载资源
                PulletFrameworks.StartCoroutine(PrepareAudioClip(path, delegate (AudioClip audioClip)
                {
                    //mAudioClipList.Add(path, audioClip);
                    PlayButSound(audioClip, volume, isLoop);
                }));
            }
        }

        private static void PlayButSound(AudioClip audioClip, float volume = 1, bool isLoop = false)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            m_BtnSource.clip = audioClip;
            m_BtnSource.volume = volume;
            m_BtnSource.Play();
            m_BtnSource.mute = false;
            m_BtnSource.loop = isLoop;
        }
        #endregion

        public static IEnumerator PrepareAudioClip(string path, Action<AudioClip> action)
        {
            if (_AudioClipList.ContainsKey(path))
            {
                action?.Invoke(_AudioClipList[path]);
                yield break;
            }
            AssetHandle assetHandle = YooAssets.LoadAssetAsync<AudioClip>(path);
            yield return assetHandle;

            AudioClip audioClip = assetHandle.AssetObject as AudioClip;
            if (audioClip != null)
            {
                _AudioClipList.Add(path, audioClip);
                action?.Invoke(audioClip);
            }
            else
            {
                PLogger.Error("加载音频失败：" + path);
            }
            assetHandle.Release();
        }
    }
}
