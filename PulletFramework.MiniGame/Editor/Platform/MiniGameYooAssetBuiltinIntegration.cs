using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    /// <summary>以软依赖方式衔接小游戏发布与可选的 Pullet YooAsset 配置。</summary>
    public static class MiniGameYooAssetBuiltinIntegration
    {
        private const string SettingsPath =
            "Assets/Settings/Pullets/YooAsset/Resources/PulletYooAssetSettings.asset";
        private const string EnabledProperty = "includeDefaultPackageInStreamingAssets";
        private const string PackageProperty = "packageName";

        public static bool IsEnabled
        {
            get
            {
                return TryGetProperty(out _, out _, out SerializedProperty property)
                    && property.boolValue;
            }
        }

        internal static void Draw()
        {
            if (!TryGetProperty(
                    out UnityEngine.Object settingsAsset,
                    out SerializedObject serializedSettings,
                    out SerializedProperty enabledProperty))
                return;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("YooAsset 内置资源", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle(
                "默认包随小游戏发布", enabledProperty.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settingsAsset, "Edit YooAsset built-in resources");
                enabledProperty.boolValue = enabled;
                serializedSettings.ApplyModifiedProperties();
                EditorUtility.SetDirty(settingsAsset);
            }

            if (!enabled)
            {
                EditorGUILayout.HelpBox(
                    "默认 Package 只发布到业务资源 CDN，不会进入小游戏 StreamingAssets。",
                    MessageType.Info);
                return;
            }

            if (TryGetBundledPackageDirectory(out string directory)
                && TryGetDirectorySize(directory, out long size))
            {
                EditorGUILayout.HelpBox(
                    $"已准备内置资源：{directory}\n大小：{FormatBytes(size)}。"
                    + "小游戏包体会增加相应体积。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "尚未生成内置资源。请先在“YooAsset 资源”中构建默认 Package，"
                    + "确认 StreamingAssets 内出现 BuiltinCatalog.bytes 后再导出小游戏。",
                    MessageType.Warning);
            }
        }

        internal static bool Validate(out string error)
        {
            if (!IsEnabled)
            {
                error = null;
                return true;
            }

            if (!TryGetBundledPackageDirectory(out string directory)
                || !File.Exists(Path.Combine(directory, "BuiltinCatalog.bytes")))
            {
                error = "已开启“默认包随小游戏发布”，但 StreamingAssets 中没有当前默认 Package。"
                    + "请先在“YooAsset 资源”中构建默认 Package。";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryGetBundledPackageDirectory(out string directory)
        {
            directory = null;
            if (!TryGetProperty(out _, out SerializedObject serializedSettings, out _))
                return false;

            SerializedProperty packageProperty = serializedSettings.FindProperty(PackageProperty);
            string packageName = packageProperty?.stringValue?.Trim();
            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            string root = Path.Combine(Application.dataPath, "StreamingAssets");
            if (!Directory.Exists(root))
                return false;

            directory = Directory.GetDirectories(root, packageName, SearchOption.AllDirectories)
                .FirstOrDefault(candidate =>
                    File.Exists(Path.Combine(candidate, "BuiltinCatalog.bytes")));
            return !string.IsNullOrWhiteSpace(directory);
        }

        private static bool TryGetProperty(
            out UnityEngine.Object settingsAsset,
            out SerializedObject serializedSettings,
            out SerializedProperty enabledProperty)
        {
            settingsAsset = AssetDatabase.LoadMainAssetAtPath(SettingsPath);
            if (settingsAsset == null)
            {
                serializedSettings = null;
                enabledProperty = null;
                return false;
            }

            serializedSettings = new SerializedObject(settingsAsset);
            serializedSettings.Update();
            enabledProperty = serializedSettings.FindProperty(EnabledProperty);
            return enabledProperty != null;
        }

        private static bool TryGetDirectorySize(string directory, out long size)
        {
            size = 0L;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return false;

            size = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Sum(path => new FileInfo(path).Length);
            return true;
        }

        private static string FormatBytes(long bytes)
        {
            const double megabyte = 1024d * 1024d;
            return bytes >= megabyte
                ? $"{bytes / megabyte:F2} MB"
                : $"{bytes / 1024d:F2} KB";
        }
    }
}
