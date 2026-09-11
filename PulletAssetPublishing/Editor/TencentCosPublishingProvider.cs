using System;

namespace PulletAssetPublishing.Editor
{
    /// <summary>腾讯云 COS 发布配置描述。</summary>
    public sealed class TencentCosPublishingProvider : IPulletAssetPublishingProvider
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
    }
}
