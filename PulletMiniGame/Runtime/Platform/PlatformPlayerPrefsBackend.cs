using System;
using System.Globalization;
using PulletFramework.Setting;

namespace PulletMiniGame.Platform
{
    /// <summary>将 PulletPlayerPrefs 映射到当前小游戏平台的本地存储。</summary>
    public sealed class PlatformPlayerPrefsBackend : IPulletPlayerPrefsBackend
    {
        private readonly IPlatformStorageService _storage;

        public PlatformPlayerPrefsBackend(IPlatformStorageService storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public bool HasKey(string key) => _storage.HasKey(key);

        public int GetInt(string key, int defaultValue)
        {
            string value = _storage.GetString(key, null);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int result) ? result : defaultValue;
        }

        public float GetFloat(string key, float defaultValue)
        {
            string value = _storage.GetString(key, null);
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float result) ? result : defaultValue;
        }

        public string GetString(string key, string defaultValue) =>
            _storage.GetString(key, defaultValue);

        public void SetInt(string key, int value) =>
            _storage.SetString(key, value.ToString(CultureInfo.InvariantCulture));

        public void SetFloat(string key, float value) =>
            _storage.SetString(key, value.ToString("R", CultureInfo.InvariantCulture));

        public void SetString(string key, string value) => _storage.SetString(key, value);
        public void DeleteKey(string key) => _storage.DeleteKey(key);
        public void Save() => _storage.Save();
    }
}
