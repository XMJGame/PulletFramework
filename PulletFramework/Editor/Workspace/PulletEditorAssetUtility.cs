using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PulletFramework.Editor
{
    /// <summary>加载与编辑器类型同名的 UXML，避免依赖第三方编辑器内部工具。</summary>
    public static class PulletEditorAssetUtility
    {
        public static Type FindType(string assemblyQualifiedName)
        {
            return Type.GetType(assemblyQualifiedName, false);
        }

        public static bool InvokeStatic(string assemblyQualifiedName, string methodName)
        {
            Type type = FindType(assemblyQualifiedName);
            var method = type?.GetMethod(methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
                return false;

            method.Invoke(null, null);
            return true;
        }

        public static VisualTreeAsset LoadWindowUxml<TWindow>() where TWindow : class
        {
            string assetName = typeof(TWindow).Name;
            string[] matches = AssetDatabase.FindAssets($"{assetName} t:VisualTreeAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), assetName,
                    StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                Debug.LogError($"[PulletEditor] 找不到窗口布局：{assetName}.uxml");
                return null;
            }

            if (matches.Length > 1)
                Debug.LogWarning($"[PulletEditor] 找到多个 {assetName}.uxml，将使用：{matches[0]}");
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(matches[0]);
        }
    }
}
