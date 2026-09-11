using System.IO;
using PulletFramework.YooAssetAdapter;
using UnityEditor;
using UnityEngine;

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

            string packageDirectory = Path.Combine(
                YooAsset.Editor.BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                settings.packageName,
                settings.appVersion);
            string reportPath = PulletYooAssetPublishReport.Create(
                packageDirectory, settings.packageName, settings.appVersion);
            Debug.Log($"[PulletYooAsset] Publish report generated: {reportPath}");
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
            Draw("packageName", "默认包名称");
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
            Draw("appVersion", "资源版本目录");

            var settings = (PulletYooAssetSettings)target;
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
                    EditorApplication.ExecuteMenuItem("YooAsset/Bundle Collector");
                if (GUILayout.Button("资源构建器", GUILayout.Height(30f)))
                    EditorApplication.ExecuteMenuItem("YooAsset/Bundle Builder");
            }
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
