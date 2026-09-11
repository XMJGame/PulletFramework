using System.IO;
using System.Linq;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace PulletFramework.Editor
{
    [CustomEditor(typeof(PulletYooAssetSettings))]
    public sealed class PulletYooAssetSettingsEditor : UnityEditor.Editor
    {
        internal const string DefaultAssetPath =
            "Assets/Settings/Pullets/YooAsset/Resources/PulletYooAssetSettings.asset";

        public static void OpenOrCreate()
        {
            PulletYooAssetSettings settings = LoadOrCreate();

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        public static void GeneratePublishReport()
        {
            PulletYooAssetSettings settings = AssetDatabase.LoadAssetAtPath<PulletYooAssetSettings>(
                DefaultAssetPath);
            if (settings == null)
                throw new InvalidDataException($"YooAsset settings not found: {DefaultAssetPath}");

            string packageName = GetSelectedPackageName(settings);
            string packageVersion = RequireLastBuildVersion(settings);
            string packageDirectory = Path.Combine(
                YooAsset.Editor.BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                packageName,
                packageVersion);
            string reportPath = PulletYooAssetPublishReport.Create(
                packageDirectory, packageName, packageVersion);
            PLogger.EditorInfo($"[PulletYooAsset] Publish report generated: {reportPath}");
            EditorUtility.RevealInFinder(reportPath);
        }

        internal static PulletYooAssetSettings LoadOrCreate()
        {
            PulletYooAssetSettings settings = AssetDatabase.LoadAssetAtPath<PulletYooAssetSettings>(
                DefaultAssetPath);
            if (settings != null)
                return settings;

            string directory = Path.GetDirectoryName(DefaultAssetPath);
            if (!AssetDatabase.IsValidFolder(directory))
                CreateFolders(directory);
            settings = CreateInstance<PulletYooAssetSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("资源包", EditorStyles.boldLabel);
            DrawPackageSelectors();
            Draw("editorPlayMode", "编辑器模式");
            Draw("playerPlayMode", "原生平台模式");
            Draw("webPlayMode", "小游戏 / WebGL 模式");

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("业务资源 CDN", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里承载 YooAsset 清单和 AssetBundle，不是微信/抖音转换 SDK 的 Unity 首包 CDN。",
                MessageType.Info);
            Draw("defaultHostServer", "主 CDN");
            Draw("fallbackHostServer", "备用 CDN");
            Draw("resourceChannel", "资源兼容通道");
            Draw("packageVersionMode", "版本生成方式");
            var settings = (PulletYooAssetSettings)target;
            if (settings.packageVersionMode == EPulletYooAssetPackageVersionMode.Manual)
                Draw("packageVersion", "资源包版本");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("当前包最后构建版本",
                    PulletYooAssetPackageBuilder.GetLastBuildVersion(settings));
            EditorGUILayout.HelpBox(
                "兼容通道通常保持 v1 不变；资源内容改变时只递增资源包版本，不需要重新发布小游戏应用。",
                MessageType.Info);

            bool requiresRemote = settings.editorPlayMode == EPulletYooAssetPlayMode.Host
                || settings.editorPlayMode == EPulletYooAssetPlayMode.Web
                || settings.playerPlayMode == EPulletYooAssetPlayMode.Host
                || settings.playerPlayMode == EPulletYooAssetPlayMode.Web
                || settings.webPlayMode == EPulletYooAssetPlayMode.Host
                || settings.webPlayMode == EPulletYooAssetPlayMode.Web;
            if (requiresRemote && string.IsNullOrWhiteSpace(settings.defaultHostServer))
                EditorGUILayout.HelpBox("远端模式已启用，请填写主 CDN 地址。", MessageType.Warning);

            if (!string.IsNullOrWhiteSpace(settings.defaultHostServer))
                EditorGUILayout.HelpBox("当前平台解析地址：\n" + settings.ResolveDefaultHostServer(),
                    MessageType.None);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("下载", EditorStyles.boldLabel);
            Draw("downloadAllOnStartup", "启动时下载全部资源");
            Draw("maximumConcurrency", "最大并发数");
            Draw("retryCount", "单文件重试次数");
            Draw("timeoutSeconds", "请求超时（秒）");
            Draw("operationTimeSliceMilliseconds", "异步时间片（毫秒）");

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(12f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("资源收集器", GUILayout.Height(30f)))
                {
                    BundleCollectorSettingData.Setting.ShowPackageView = true;
                    BundleCollectorSettingData.SaveFile();
                    EditorApplication.ExecuteMenuItem("YooAsset/Bundle Collector");
                }
                if (GUILayout.Button("资源构建器", GUILayout.Height(30f)))
                    EditorApplication.ExecuteMenuItem("YooAsset/Bundle Builder");
            }
            if (GUILayout.Button("构建当前版本", GUILayout.Height(34f)))
                PulletYooAssetPackageBuilder.BuildFromWorkspace();
            if (GUILayout.Button("生成 CDN 发布清单", GUILayout.Height(30f)))
                GeneratePublishReport();
            if (GUILayout.Button("上传当前版本到腾讯云 COS", GUILayout.Height(30f)))
                PulletYooAssetCosPublisher.PublishFromMenu();
            if (GUILayout.Button("配置腾讯云 COS 下载跨域", GUILayout.Height(30f)))
                PulletYooAssetCosPublisher.ConfigureDownloadCors();
        }

        private void Draw(string propertyName, string label)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName),
                new GUIContent(label));
        }

        private void DrawPackageSelectors()
        {
            string[] packageNames = BundleCollectorSettingData.Setting.Packages
                .Select(item => item.PackageName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct()
                .ToArray();
            SerializedProperty selectedProperty =
                serializedObject.FindProperty("editorSelectedPackageName");
            if (packageNames.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "YooAsset 尚未创建 Package，请打开资源收集器后点击 + 新建。",
                    MessageType.Warning);
                return;
            }

            SerializedProperty defaultProperty = serializedObject.FindProperty("packageName");
            int defaultIndex = System.Array.IndexOf(packageNames, defaultProperty.stringValue);
            if (defaultIndex < 0)
                defaultIndex = 0;
            defaultProperty.stringValue = packageNames[
                EditorGUILayout.Popup("默认启动包", defaultIndex, packageNames)];

            int currentIndex = System.Array.IndexOf(packageNames, selectedProperty.stringValue);
            if (currentIndex < 0)
            {
                currentIndex = System.Array.IndexOf(packageNames, defaultProperty.stringValue);
                if (currentIndex < 0)
                    currentIndex = 0;
            }
            int nextIndex = EditorGUILayout.Popup("当前构建 / 发布包", currentIndex, packageNames);
            selectedProperty.stringValue = packageNames[nextIndex];

        }

        public static string GetSelectedPackageName(PulletYooAssetSettings settings)
        {
            return string.IsNullOrWhiteSpace(settings.editorSelectedPackageName)
                ? settings.packageName?.Trim() ?? string.Empty
                : settings.editorSelectedPackageName.Trim();
        }

        public static string RequireLastBuildVersion(PulletYooAssetSettings settings)
        {
            string version = PulletYooAssetPackageBuilder.GetLastBuildVersion(settings);
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("尚未构建资源版本，请先点击‘构建当前版本’。");
            return version;
        }

        private static void CreateFolders(string path)
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
