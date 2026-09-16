using System;
using UnityEngine;

namespace PulletFramework.HybridCLR
{
    public enum EPulletHotUpdateAssemblyKind
    {
        AotMetadata,
        HotUpdate
    }

    [Serializable]
    public sealed class PulletHotUpdateAssemblyInfo
    {
        public string name;
        public string version;
        public EPulletHotUpdateAssemblyKind kind;
        public string location;
        public string sha256;
        public long byteLength;
        public string[] dependencies = Array.Empty<string>();
    }

    [Serializable]
    public sealed class PulletHotUpdateManifest
    {
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public string contentVersion;
        public string[] compatiblePlayerVersions = Array.Empty<string>();
        public string entryAssembly;
        public string entryType;
        public string entryMethod;
        public PulletHotUpdateAssemblyInfo[] assemblies = Array.Empty<PulletHotUpdateAssemblyInfo>();

        public static PulletHotUpdateManifest FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("热更新清单内容不能为空。", nameof(json));
            PulletHotUpdateManifest manifest = JsonUtility.FromJson<PulletHotUpdateManifest>(json);
            if (manifest == null)
                throw new FormatException("热更新清单 JSON 无法解析。");
            return manifest;
        }

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public bool SupportsPlayerVersion(string playerVersion)
        {
            if (compatiblePlayerVersions == null || compatiblePlayerVersions.Length == 0)
                return true;
            for (int i = 0; i < compatiblePlayerVersions.Length; i++)
            {
                if (string.Equals(compatiblePlayerVersions[i], playerVersion,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
