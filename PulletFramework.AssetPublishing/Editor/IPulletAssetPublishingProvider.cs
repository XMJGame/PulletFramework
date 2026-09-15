using System;
using System.Threading.Tasks;

namespace PulletFramework.AssetPublishing.Editor
{
    /// <summary>对象存储供应商扩展点；新增供应商无需修改工作台。</summary>
    public interface IPulletAssetPublishingProvider
    {
        string Id { get; }
        string DisplayName { get; }
        void ApplyDefaults(PulletAssetPublishingProfile profile);
        bool Validate(PulletAssetPublishingProfile profile, out string message);
    }

    /// <summary>提供文件上传和公开下载地址的对象存储供应商。</summary>
    public interface IPulletObjectStorageProvider : IPulletAssetPublishingProvider
    {
        string BuildPublicUrl(PulletAssetPublishingProfile profile, string objectKey);
        Task<string> UploadAsync(
            PulletAssetPublishingProfile profile,
            string objectKey,
            string sourcePath,
            Action<long, long> progress = null,
            string cacheControl = null,
            string contentType = null);
        bool EnsureMiniGameDownloadCors(PulletAssetPublishingProfile profile);
    }
}
