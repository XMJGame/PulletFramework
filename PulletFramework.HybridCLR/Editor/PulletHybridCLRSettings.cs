using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>仅用于生成热更新清单的项目级编辑器配置。</summary>
    [FilePath("ProjectSettings/PulletHybridCLRSettings.asset",
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class PulletHybridCLRSettings : ScriptableSingleton<PulletHybridCLRSettings>
    {
        [SerializeField] private string entryAssembly;
        [SerializeField] private string entryType;
        [SerializeField] private string entryMethod = "Run";

        public string EntryAssembly => entryAssembly;
        public string EntryType => entryType;
        public string EntryMethod => entryMethod;

        public void SetEntry(string assemblyName, string typeName, string methodName)
        {
            entryAssembly = assemblyName?.Trim();
            entryType = typeName?.Trim();
            entryMethod = methodName?.Trim();
            Save(true);
        }
    }
}
