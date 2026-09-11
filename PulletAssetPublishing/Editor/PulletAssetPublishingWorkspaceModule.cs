using System;
using System.Linq;
using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PulletAssetPublishing.Editor
{
    /// <summary>资源发布模块使用原生 UI Toolkit 页面。</summary>
    public sealed class PulletAssetPublishingWorkspaceModule :
        IPulletWorkspaceModule, IPulletWorkspaceVisualModule
    {
        private readonly System.Collections.Generic.List<IPulletAssetPublishingProvider> _providers =
            new System.Collections.Generic.List<IPulletAssetPublishingProvider>();

        public string Id => "asset-publishing";
        public string DisplayName => "资源发布";
        public string Description => "选择对象存储供应商，维护发布凭据、存储桶和下载域名。";
        public int Order => 250;

        public void OnEnable()
        {
            _providers.Clear();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IPulletAssetPublishingProvider>()
                         .Where(type => !type.IsAbstract && !type.IsInterface))
            {
                if (Activator.CreateInstance(type) is IPulletAssetPublishingProvider provider
                    && _providers.All(item => item.Id != provider.Id))
                    _providers.Add(provider);
            }
            _providers.Sort((left, right) =>
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));
        }
        public void OnDisable()
        {
            PulletAssetPublishingSettingsData.SaveIfDirty();
        }
        public void OnGUI() { }

        public void CreateGUI(VisualElement root)
        {
            VisualTreeAsset layout = LoadLayout();
            if (layout == null)
            {
                root.Add(new Label("找不到资源发布 UXML 布局。"));
                return;
            }
            layout.CloneTree(root);
            AddStyleSheet(root);

            PulletAssetPublishingSettings setting = PulletAssetPublishingSettingsData.Setting;
            if (_providers.Count == 0)
            {
                SetStatus(root, "没有发现可用的对象存储 Provider。", true);
                return;
            }

            IPulletAssetPublishingProvider activeProvider = _providers.FirstOrDefault(
                item => item.Id == setting.activeProviderId) ?? _providers[0];
            setting.activeProviderId = activeProvider.Id;
            PulletAssetPublishingProfile profile = setting.GetOrCreateProfile(activeProvider.Id);
            activeProvider.ApplyDefaults(profile);

            var provider = root.Q<DropdownField>("provider");
            provider.choices = _providers.Select(item => item.DisplayName).ToList();
            provider.SetValueWithoutNotify(activeProvider.DisplayName);
            BindProfileFields(root, setting, profile);

            provider.RegisterValueChangedCallback(evt =>
            {
                IPulletAssetPublishingProvider selected = _providers.First(
                    item => item.DisplayName == evt.newValue);
                setting.activeProviderId = selected.Id;
                PulletAssetPublishingProfile selectedProfile = setting.GetOrCreateProfile(selected.Id);
                selected.ApplyDefaults(selectedProfile);
                SetProfileFieldValues(root, selectedProfile);
                PulletAssetPublishingSettingsData.MarkDirty();
                SetStatus(root, $"已切换到：{selected.DisplayName}", false);
            });
            root.Q<Button>("save").clicked += () =>
            {
                PulletAssetPublishingSettingsData.Save();
                SetStatus(root, "配置已保存。", false);
            };
            root.Q<Button>("validate").clicked += () =>
            {
                IPulletAssetPublishingProvider selected = _providers.First(
                    item => item.Id == setting.activeProviderId);
                bool valid = selected.Validate(setting.GetOrCreateProfile(selected.Id), out string message);
                SetStatus(root, message, !valid);
            };
            root.Q<Button>("locate").clicked += () =>
            {
                Selection.activeObject = setting;
                EditorGUIUtility.PingObject(setting);
            };
            SetStatus(root, $"当前已安装供应商：{string.Join("、", provider.choices)}", false);
        }

        private static void BindProfileFields(VisualElement root,
            PulletAssetPublishingSettings setting, PulletAssetPublishingProfile profile)
        {
            Bind(root.Q<TextField>("access-key-id"), profile.accessKeyId,
                value => setting.GetOrCreateProfile(setting.activeProviderId).accessKeyId = value);
            TextField secret = root.Q<TextField>("access-key-secret");
            secret.isPasswordField = true;
            Bind(secret, profile.accessKeySecret,
                value => setting.GetOrCreateProfile(setting.activeProviderId).accessKeySecret = value);
            Bind(root.Q<TextField>("bucket"), profile.bucket,
                value => setting.GetOrCreateProfile(setting.activeProviderId).bucket = value);
            Bind(root.Q<TextField>("region"), profile.region,
                value => setting.GetOrCreateProfile(setting.activeProviderId).region = value);
            Bind(root.Q<TextField>("endpoint"), profile.endpoint,
                value => setting.GetOrCreateProfile(setting.activeProviderId).endpoint = value);
            Bind(root.Q<TextField>("public-base-url"), profile.publicBaseUrl,
                value => setting.GetOrCreateProfile(setting.activeProviderId).publicBaseUrl = value);
            Bind(root.Q<TextField>("root-folder"), profile.rootFolder,
                value => setting.GetOrCreateProfile(setting.activeProviderId).rootFolder = value);
        }

        private static void SetProfileFieldValues(VisualElement root,
            PulletAssetPublishingProfile profile)
        {
            root.Q<TextField>("access-key-id").SetValueWithoutNotify(profile.accessKeyId ?? string.Empty);
            root.Q<TextField>("access-key-secret").SetValueWithoutNotify(profile.accessKeySecret ?? string.Empty);
            root.Q<TextField>("bucket").SetValueWithoutNotify(profile.bucket ?? string.Empty);
            root.Q<TextField>("region").SetValueWithoutNotify(profile.region ?? string.Empty);
            root.Q<TextField>("endpoint").SetValueWithoutNotify(profile.endpoint ?? string.Empty);
            root.Q<TextField>("public-base-url").SetValueWithoutNotify(profile.publicBaseUrl ?? string.Empty);
            root.Q<TextField>("root-folder").SetValueWithoutNotify(profile.rootFolder ?? string.Empty);
        }

        private static void Bind(TextField field, string value, Action<string> setter)
        {
            field.SetValueWithoutNotify(value ?? string.Empty);
            field.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                PulletAssetPublishingSettingsData.MarkDirty();
            });
        }

        private static void SetStatus(VisualElement root, string message, bool error)
        {
            Label status = root.Q<Label>("status");
            status.text = message;
            status.EnableInClassList("publishing-status--error", error);
        }

        private static VisualTreeAsset LoadLayout()
        {
            string path = AssetDatabase.FindAssets(
                    "PulletAssetPublishingWorkspaceModule t:VisualTreeAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(item => string.Equals(
                    System.IO.Path.GetFileNameWithoutExtension(item),
                    nameof(PulletAssetPublishingWorkspaceModule),
                    StringComparison.Ordinal));
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }

        private static void AddStyleSheet(VisualElement root)
        {
            string path = AssetDatabase.FindAssets(
                    "PulletAssetPublishingWorkspaceModule t:StyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(item => string.Equals(
                    System.IO.Path.GetFileNameWithoutExtension(item),
                    nameof(PulletAssetPublishingWorkspaceModule),
                    StringComparison.Ordinal));
            if (string.IsNullOrEmpty(path))
                return;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);
        }
    }
}
