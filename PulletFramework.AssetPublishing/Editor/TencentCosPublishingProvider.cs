using System;
using System.Threading.Tasks;

namespace PulletFramework.AssetPublishing.Editor
{
    /// <summary>腾讯云 COS 发布配置描述。</summary>
    public sealed class TencentCosPublishingProvider : IPulletObjectStorageProvider
    {
        public string Id => "tencent-cos";
        public string DisplayName => "腾讯云 COS";

        public void ApplyDefaults(PulletAssetPublishingProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.region))
                profile.region = "ap-guangzhou";
        }

        public bool Validate(PulletAssetPublishingProfile profile, out string message)
        {
            if (string.IsNullOrWhiteSpace(profile.accessKeyId)
                || string.IsNullOrWhiteSpace(profile.accessKeySecret)
                || string.IsNullOrWhiteSpace(profile.bucket)
                || string.IsNullOrWhiteSpace(profile.region))
            {
                message = "请完整填写 SecretId、SecretKey、Bucket 和 Region。";
                return false;
            }

            message = "腾讯云 COS 配置字段完整，可以执行连接测试或资源上传。";
            return true;
        }

        public string BuildPublicUrl(PulletAssetPublishingProfile profile, string objectKey)
        {
            TencentCosConfiguration configuration = TencentCOS.GetConfiguration(profile);
            if (string.IsNullOrWhiteSpace(configuration.BaseUrl))
                throw new InvalidOperationException("请填写公开下载域名，或填写 Bucket 和 Region 自动生成 COS 域名。");
            string fullKey = TencentCOS.CombineKey(configuration.Folder, objectKey);
            return configuration.BaseUrl.TrimEnd('/') + "/" + fullKey;
        }

        public async Task<string> UploadAsync(
            PulletAssetPublishingProfile profile,
            string objectKey,
            string sourcePath,
            Action<long, long> progress = null,
            string cacheControl = null,
            string contentType = null)
        {
            TencentCosConfiguration configuration = TencentCOS.GetConfiguration(profile);
            string uploadedKey = await TencentCOS.PutObjectAsync(
                configuration, objectKey, sourcePath, progress, cacheControl, contentType);
            return configuration.BaseUrl.TrimEnd('/') + "/" + uploadedKey;
        }

        public bool EnsureMiniGameDownloadCors(PulletAssetPublishingProfile profile)
        {
            return TencentCOS.EnsureMiniGameDownloadCors(TencentCOS.GetConfiguration(profile));
        }
    }
}
