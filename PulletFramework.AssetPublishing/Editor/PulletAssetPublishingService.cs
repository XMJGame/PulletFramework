using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace PulletFramework.AssetPublishing.Editor
{
    /// <summary>一次发布使用的供应商配置快照。</summary>
    public sealed class PulletAssetPublishingSession
    {
        private readonly IPulletObjectStorageProvider _provider;
        private readonly PulletAssetPublishingProfile _profile;

        public string ProviderId => _provider.Id;
        public string ProviderName => _provider.DisplayName;

        internal PulletAssetPublishingSession(
            IPulletObjectStorageProvider provider, PulletAssetPublishingProfile profile)
        {
            _provider = provider;
            _profile = profile.Clone();
        }

        public string BuildPublicUrl(string objectKey)
        {
            return _provider.BuildPublicUrl(_profile, objectKey);
        }

        public Task<string> UploadAsync(
            string objectKey,
            string sourcePath,
            Action<long, long> progress = null,
            string cacheControl = null,
            string contentType = null)
        {
            return _provider.UploadAsync(
                _profile, objectKey, sourcePath, progress, cacheControl, contentType);
        }

        public bool EnsureMiniGameDownloadCors()
        {
            return _provider.EnsureMiniGameDownloadCors(_profile);
        }

        public IDisposable AcquirePublication(string destination)
        {
            return PulletAssetPublishingService.AcquirePublication(
                ProviderId, BuildPublicUrl(destination).TrimEnd('/'));
        }
    }

    /// <summary>向业务模块暴露当前对象存储供应商，不泄露具体厂商实现。</summary>
    public static class PulletAssetPublishingService
    {
        private static readonly HashSet<string> ActivePublications =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ActiveProviders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object PublicationLock = new object();

        public static string ActiveProviderName
        {
            get
            {
                TryGetActiveProvider(out IPulletObjectStorageProvider provider,
                    out _, out _, false);
                return provider?.DisplayName ?? "未配置";
            }
        }

        public static bool TryBuildPublicUrl(
            string objectKey, out string url, out string error)
        {
            url = null;
            if (!TryGetActiveProvider(out IPulletObjectStorageProvider provider,
                    out PulletAssetPublishingProfile profile, out error, false))
                return false;

            try
            {
                url = provider.BuildPublicUrl(profile, objectKey);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static PulletAssetPublishingSession CreateSession(bool validateProfile = true)
        {
            if (!TryGetActiveProvider(out IPulletObjectStorageProvider provider,
                    out PulletAssetPublishingProfile profile, out string error, validateProfile))
                throw new InvalidOperationException(error);
            return new PulletAssetPublishingSession(provider, profile);
        }

        public static async Task<string> UploadAsync(
            string objectKey,
            string sourcePath,
            Action<long, long> progress = null,
            string cacheControl = null,
            string contentType = null)
        {
            PulletAssetPublishingSession session = CreateSession();
            return await session.UploadAsync(
                objectKey, sourcePath, progress, cacheControl, contentType);
        }

        public static bool EnsureMiniGameDownloadCors()
        {
            return CreateSession().EnsureMiniGameDownloadCors();
        }

        internal static IDisposable AcquirePublication(string providerId, string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException(
                    "Publication destination is required.", nameof(destination));
            string key = providerId + "|" + destination;
            lock (PublicationLock)
            {
                if (ActivePublications.Contains(key) || ActiveProviders.Contains(providerId))
                    throw new InvalidOperationException(
                        "该供应商已有发布任务正在执行，请等待完成后重试。");
                ActivePublications.Add(key);
                ActiveProviders.Add(providerId);
            }
            return new PublicationLease(key, providerId);
        }

        private sealed class PublicationLease : IDisposable
        {
            private string _key;
            private readonly string _providerId;

            public PublicationLease(string key, string providerId)
            {
                _key = key;
                _providerId = providerId;
            }

            public void Dispose()
            {
                if (_key == null)
                    return;
                lock (PublicationLock)
                {
                    ActivePublications.Remove(_key);
                    ActiveProviders.Remove(_providerId);
                }
                _key = null;
            }
        }

        private static bool TryGetActiveProvider(
            out IPulletObjectStorageProvider provider,
            out PulletAssetPublishingProfile profile,
            out string error,
            bool validateProfile)
        {
            PulletAssetPublishingSettings setting = PulletAssetPublishingSettingsData.Setting;
            provider = PulletAssetPublishingProviderRegistry
                .CreateProviders<IPulletObjectStorageProvider>()
                .FirstOrDefault(item => item != null && item.Id == setting.activeProviderId);
            profile = setting.GetOrCreateProfile(setting.activeProviderId);

            if (provider == null)
            {
                error = $"当前供应商不支持文件上传：{setting.activeProviderId}";
                return false;
            }

            provider.ApplyDefaults(profile);
            if (validateProfile && !provider.Validate(profile, out error))
                return false;

            error = null;
            return true;
        }
    }
}
