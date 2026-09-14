using System;
using System.IO;
using System.Threading.Tasks;
using COSXML;
using COSXML.Auth;
using COSXML.CosException;
using COSXML.Model.Bucket;
using COSXML.Model.Object;
using COSXML.Model.Tag;
using COSXML.Transfer;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PulletAssetPublishing.Editor
{
    public sealed class TencentCosConfiguration
    {
        public string SecretId;
        public string SecretKey;
        public string Bucket;
        public string Region;
        public string Folder;
        public string BaseUrl;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SecretId)) throw new InvalidOperationException("COS SecretId is required.");
            if (string.IsNullOrWhiteSpace(SecretKey)) throw new InvalidOperationException("COS SecretKey is required.");
            if (string.IsNullOrWhiteSpace(Bucket)) throw new InvalidOperationException("COS bucket is required.");
            if (string.IsNullOrWhiteSpace(Region)) throw new InvalidOperationException("COS region is required.");
        }
    }

    /// <summary>腾讯云 COS 编辑器上传服务，只读取当前资源发布配置。</summary>
    public static class TencentCOS
    {
        private const string MiniGameCorsRuleId = "pullet-minigame-public-assets";
        private static CosXmlServer s_Server;
        private static string s_ConfigurationFingerprint;

        public static TencentCosConfiguration GetConfiguration()
        {
            string providerId = PulletAssetPublishingSettingsBridge.GetValue("providerId");
            if (!string.Equals(providerId, "tencent-cos", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"当前资源发布供应商不是腾讯云 COS：{providerId}");

            string bucket = PulletAssetPublishingSettingsBridge.GetValue("bucket");
            string region = PulletAssetPublishingSettingsBridge.GetValue("region");
            string configuredBaseUrl = PulletAssetPublishingSettingsBridge.GetValue("publicBaseUrl");
            string baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? (string.IsNullOrWhiteSpace(bucket)
                    ? string.Empty
                    : $"https://{bucket}.cos.{region}.myqcloud.com")
                : configuredBaseUrl;
            return new TencentCosConfiguration
            {
                SecretId = PulletAssetPublishingSettingsBridge.GetValue("accessKeyId"),
                SecretKey = PulletAssetPublishingSettingsBridge.GetValue("accessKeySecret"),
                Bucket = bucket,
                Region = region,
                Folder = NormalizeKey(PulletAssetPublishingSettingsBridge.GetValue("rootFolder")),
                BaseUrl = baseUrl.TrimEnd('/')
            };
        }

        public static async Task<string> PutObject(string key, string sourcePath)
        {
            return await PutObjectAsync(key, sourcePath);
        }

        public static async Task<string> PutObjectAsync(
            string key, string sourcePath, Action<long, long> progress = null,
            string cacheControl = null, string contentType = null)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("COS upload source file not found.", sourcePath);

            TencentCosConfiguration configuration = GetConfiguration();
            configuration.Validate();
            EnsureInitialized(configuration);
            string objectKey = CombineKey(configuration.Folder, key);

            var transferManager = new TransferManager(s_Server, new TransferConfig());
            var request = new PutObjectRequest(configuration.Bucket, objectKey, sourcePath);
            if (!string.IsNullOrWhiteSpace(cacheControl))
                request.SetRequestHeader("Cache-Control", cacheControl);
            if (!string.IsNullOrWhiteSpace(contentType))
                request.SetRequestHeader("Content-Type", contentType);
            var uploadTask = new COSXMLUploadTask(request);
            uploadTask.SetSrcPath(sourcePath);
            uploadTask.progressCallback = (completed, total) => progress?.Invoke(completed, total);
            await transferManager.UploadAsync(uploadTask);
            return objectKey;
        }

        public static string CombineKey(params string[] parts)
        {
            return NormalizeKey(string.Join("/", parts ?? Array.Empty<string>()));
        }

        /// <summary>合并小游戏静态资源所需的公开下载 CORS 规则，不删除桶内其他规则。</summary>
        public static bool EnsureMiniGameDownloadCors()
        {
            TencentCosConfiguration configuration = GetConfiguration();
            configuration.Validate();
            EnsureInitialized(configuration);

            List<CORSConfiguration.CORSRule> rules;
            try
            {
                var result = s_Server.GetBucketCORS(new GetBucketCORSRequest(configuration.Bucket));
                rules = result.corsConfiguration?.corsRules ?? new List<CORSConfiguration.CORSRule>();
            }
            catch (CosServerException exception) when (exception.statusCode == 404)
            {
                rules = new List<CORSConfiguration.CORSRule>();
            }

            CORSConfiguration.CORSRule current = rules.FirstOrDefault(rule =>
                string.Equals(rule.id, MiniGameCorsRuleId, StringComparison.Ordinal));
            if (IsExpectedMiniGameRule(current))
            {
                PulletFramework.PLogger.EditorInfo(
                    "[TencentCOS] Mini game download CORS rule is already configured.");
                return false;
            }

            rules.RemoveAll(rule => string.Equals(rule.id, MiniGameCorsRuleId, StringComparison.Ordinal));
            rules.Add(new CORSConfiguration.CORSRule
            {
                id = MiniGameCorsRuleId,
                allowedOrigins = new List<string> { "*" },
                allowedMethods = new List<string> { "GET", "HEAD" },
                allowedHeaders = new List<string> { "*" },
                exposeHeaders = new List<string> { "ETag", "Content-Length" },
                maxAgeSeconds = 3600
            });
            var request = new PutBucketCORSRequest(configuration.Bucket);
            request.SetCORSRules(rules);
            s_Server.PutBucketCORS(request);
            PulletFramework.PLogger.EditorInfo(
                "[TencentCOS] Mini game download CORS rule configured without removing existing rules.");
            return true;
        }

        private static void EnsureInitialized(TencentCosConfiguration configuration)
        {
            string fingerprint = $"{configuration.SecretId}|{configuration.Bucket}|{configuration.Region}";
            if (s_Server != null && s_ConfigurationFingerprint == fingerprint)
                return;

            var config = new CosXmlConfig.Builder()
                .SetRegion(configuration.Region)
                .SetDebugLog(false)
                .Build();
            var credentials = new DefaultQCloudCredentialProvider(
                configuration.SecretId, configuration.SecretKey, 600);
            s_Server = new CosXmlServer(config, credentials);
            s_ConfigurationFingerprint = fingerprint;
        }

        private static string NormalizeKey(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\\', '/').Trim('/');
        }

        private static bool IsExpectedMiniGameRule(CORSConfiguration.CORSRule rule)
        {
            return rule != null
                && rule.allowedOrigins != null && rule.allowedOrigins.Contains("*")
                && rule.allowedMethods != null && rule.allowedMethods.Contains("GET")
                && rule.allowedMethods.Contains("HEAD")
                && rule.allowedHeaders != null && rule.allowedHeaders.Contains("*");
        }
    }
}
