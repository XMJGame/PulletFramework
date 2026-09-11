using System;
using System.Collections.Generic;
using System.Linq;
using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PulletFramework.Editor
{
    /// <summary>汇总已安装 Pullet 模块的统一编辑器工作台。</summary>
    public sealed class PulletWorkspaceWindow : EditorWindow
    {
        private const string SelectedModuleKey = "PulletFramework.Workspace.SelectedModule";
        private readonly List<IPulletWorkspaceModule> _modules = new List<IPulletWorkspaceModule>();
        private VisualElement _moduleList;
        private VisualElement _moduleContent;
        private Label _moduleTitle;
        private Label _moduleDescription;
        private string _selectedModuleId;

        [MenuItem("Pullets/Workspace", false, 0)]
        public static void Open()
        {
            var window = GetWindow<PulletWorkspaceWindow>("Pullet Workspace");
            window.minSize = new Vector2(760f, 560f);
        }

        /// <summary>供 CI 或升级检查调用，不创建可视窗口。</summary>
        public static void ValidateModules()
        {
            List<IPulletWorkspaceModule> modules = DiscoverModules(true);
            if (modules.Count == 0)
                throw new InvalidOperationException("没有发现 Pullet Workspace 模块。");
            foreach (IPulletWorkspaceModule module in modules)
            {
                module.OnEnable();
                module.OnDisable();
            }
            Debug.Log("[PulletWorkspace] 模块检查通过：" +
                      string.Join(", ", modules.Select(module => module.Id)));
        }

        private void OnEnable()
        {
            ReloadModules(false);
        }

        private void OnDisable()
        {
            foreach (IPulletWorkspaceModule module in _modules)
                TryInvoke(module, module.OnDisable);
            _modules.Clear();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            VisualTreeAsset layout = PulletEditorAssetUtility.LoadWindowUxml<PulletWorkspaceWindow>();
            if (layout == null)
                return;
            layout.CloneTree(rootVisualElement);
            AddStyleSheet(rootVisualElement, nameof(PulletWorkspaceWindow));

            _moduleList = rootVisualElement.Q<VisualElement>("module-list");
            _moduleContent = rootVisualElement.Q<VisualElement>("module-content");
            _moduleTitle = rootVisualElement.Q<Label>("module-title");
            _moduleDescription = rootVisualElement.Q<Label>("module-description");
            rootVisualElement.Q<Button>("reload-modules").clicked += () => ReloadModules(true);
            RenderModules();
        }

        private void ReloadModules(bool render)
        {
            foreach (IPulletWorkspaceModule module in _modules)
                TryInvoke(module, module.OnDisable);
            _modules.Clear();
            _modules.AddRange(DiscoverModules(false));
            foreach (IPulletWorkspaceModule module in _modules)
                TryInvoke(module, module.OnEnable);

            string persisted = EditorPrefs.GetString(SelectedModuleKey, string.Empty);
            _selectedModuleId = _modules.Any(module => module.Id == persisted)
                ? persisted
                : _modules.FirstOrDefault()?.Id;
            if (render && _moduleList != null)
                RenderModules();
        }

        private void RenderModules()
        {
            _moduleList?.Clear();
            if (_modules.Count == 0)
            {
                _moduleTitle.text = "没有可用模块";
                _moduleDescription.text = "请安装至少一个 Pullet 编辑器模块。";
                _moduleContent.Clear();
                return;
            }

            foreach (IPulletWorkspaceModule module in _modules)
            {
                var button = new Button(() => SelectModule(module.Id)) { text = module.DisplayName };
                button.AddToClassList("workspace-module-button");
                if (module.Id == _selectedModuleId)
                    button.AddToClassList("workspace-module-button--selected");
                _moduleList.Add(button);
            }
            ShowSelectedModule();
        }

        private void SelectModule(string id)
        {
            if (_selectedModuleId == id)
                return;
            _selectedModuleId = id;
            EditorPrefs.SetString(SelectedModuleKey, id);
            RenderModules();
        }

        private void ShowSelectedModule()
        {
            IPulletWorkspaceModule module = _modules.FirstOrDefault(item => item.Id == _selectedModuleId)
                                              ?? _modules[0];
            _moduleTitle.text = module.DisplayName;
            _moduleDescription.text = module.Description ?? string.Empty;
            _moduleContent.Clear();
            var pageHost = new VisualElement { name = $"module-page-{module.Id}" };
            pageHost.style.flexGrow = 1f;
            _moduleContent.Add(pageHost);

            if (module is IPulletWorkspaceVisualModule visualModule)
            {
                TryInvoke(module, () => visualModule.CreateGUI(pageHost));
                return;
            }

            var compatibilityContainer = new IMGUIContainer(() => TryInvoke(module, module.OnGUI));
            compatibilityContainer.style.flexGrow = 1f;
            var scrollView = new ScrollView();
            scrollView.style.flexGrow = 1f;
            scrollView.Add(compatibilityContainer);
            pageHost.Add(scrollView);
        }

        private static List<IPulletWorkspaceModule> DiscoverModules(bool throwOnError)
        {
            var modules = new List<IPulletWorkspaceModule>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IPulletWorkspaceModule>()
                         .Where(type => !type.IsAbstract && !type.IsInterface))
            {
                try
                {
                    var module = (IPulletWorkspaceModule)Activator.CreateInstance(type);
                    if (module == null || string.IsNullOrWhiteSpace(module.Id))
                        continue;
                    if (!ids.Add(module.Id))
                        throw new InvalidOperationException($"Workspace 模块 ID 重复：{module.Id}");
                    modules.Add(module);
                }
                catch (Exception exception)
                {
                    if (throwOnError)
                        throw;
                    Debug.LogException(exception);
                }
            }

            modules.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            });
            return modules;
        }

        private static void TryInvoke(IPulletWorkspaceModule module, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PulletWorkspace] 模块 {module.Id} 执行失败。\n{exception}");
            }
        }

        private static void AddStyleSheet(VisualElement root, string assetName)
        {
            string path = AssetDatabase.FindAssets($"{assetName} t:StyleSheet")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(item => string.Equals(
                    System.IO.Path.GetFileNameWithoutExtension(item),
                    assetName,
                    StringComparison.Ordinal));
            if (string.IsNullOrEmpty(path))
                return;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);
        }
    }
}
