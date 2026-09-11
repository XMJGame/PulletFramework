using System;
using UnityEngine;

namespace PulletFramework.Setting
{
    /// <summary>可替换的键值存储后端。</summary>
    public interface IPulletPlayerPrefsBackend
    {
        bool HasKey(string key);
        int GetInt(string key, int defaultValue);
        float GetFloat(string key, float defaultValue);
        string GetString(string key, string defaultValue);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }

    /// <summary>
    /// 框架统一的轻量设置存储。默认使用 Unity PlayerPrefs，宿主模块可安装平台后端。
    /// </summary>
    public static class PulletPlayerPrefs
    {
        private static readonly IPulletPlayerPrefsBackend UnityBackend =
            new UnityPlayerPrefsBackend();
        private static IPulletPlayerPrefsBackend _backend = UnityBackend;

        public static bool IsUsingCustomBackend => !ReferenceEquals(_backend, UnityBackend);

        public static void InstallBackend(IPulletPlayerPrefsBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public static void UninstallBackend(bool save = true)
        {
            if (save && IsUsingCustomBackend)
                _backend.Save();
            _backend = UnityBackend;
        }

        public static bool HasKey(string key) => _backend.HasKey(key);
        public static int GetInt(string key, int defaultValue = 0) => _backend.GetInt(key, defaultValue);
        public static float GetFloat(string key, float defaultValue = 0f) => _backend.GetFloat(key, defaultValue);
        public static string GetString(string key, string defaultValue = "") =>
            _backend.GetString(key, defaultValue);
        public static void SetInt(string key, int value) => _backend.SetInt(key, value);
        public static void SetFloat(string key, float value) => _backend.SetFloat(key, value);
        public static void SetString(string key, string value) => _backend.SetString(key, value);
        public static void DeleteKey(string key) => _backend.DeleteKey(key);
        public static void Save() => _backend.Save();

        private sealed class UnityPlayerPrefsBackend : IPulletPlayerPrefsBackend
        {
            public bool HasKey(string key) => UnityEngine.PlayerPrefs.HasKey(key);
            public int GetInt(string key, int defaultValue) =>
                UnityEngine.PlayerPrefs.GetInt(key, defaultValue);
            public float GetFloat(string key, float defaultValue) =>
                UnityEngine.PlayerPrefs.GetFloat(key, defaultValue);
            public string GetString(string key, string defaultValue) =>
                UnityEngine.PlayerPrefs.GetString(key, defaultValue);
            public void SetInt(string key, int value) => UnityEngine.PlayerPrefs.SetInt(key, value);
            public void SetFloat(string key, float value) => UnityEngine.PlayerPrefs.SetFloat(key, value);
            public void SetString(string key, string value) => UnityEngine.PlayerPrefs.SetString(key, value);
            public void DeleteKey(string key) => UnityEngine.PlayerPrefs.DeleteKey(key);
            public void Save() => UnityEngine.PlayerPrefs.Save();
        }
    }
}
