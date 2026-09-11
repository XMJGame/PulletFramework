using System;
using System.Collections.Generic;
using System.Linq;
using PulletFramework.Editor.Workspace;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
    /// <summary>汇总已安装 Pullet 模块的统一编辑器工作台。</summary>
    public sealed class PulletWorkspaceWindow : EditorWindow
    {
        private const string SelectedModuleKey = "PulletFramework.Workspace.SelectedModule";
        private readonly List<IPulletWorkspaceModule> _modules = new List<IPulletWorkspaceModule>();
        private Vector2 _sidebarScroll;
        private Vector2 _contentScroll;
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
            ReloadModules();
        }

        private void OnDisable()
        {
            foreach (IPulletWorkspaceModule module in _modules)
                TryInvoke(module, module.OnDisable);
            _modules.Clear();
        }

        private void ReloadModules()
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

        private void OnGUI()
        {
            if (_modules.Count == 0)
            {
                EditorGUILayout.HelpBox("没有发现可用的 Pullet 编辑器模块。", MessageType.Warning);
                if (GUILayout.Button("重新扫描"))
                    ReloadModules();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSidebar();
                DrawContent();
            }
        }

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(184f),
                       GUILayout.ExpandHeight(true)))
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField("Pullet Workspace", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("模块化开发与发布", EditorStyles.miniLabel);
                GUILayout.Space(8f);

                _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);
                foreach (IPulletWorkspaceModule module in _modules)
                {
                    bool selected = module.Id == _selectedModuleId;
                    GUIStyle style = selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                    if (GUILayout.Toggle(selected, module.DisplayName, style, GUILayout.Height(30f)) && !selected)
                    {
                        _selectedModuleId = module.Id;
                        _contentScroll = Vector2.zero;
                        EditorPrefs.SetString(SelectedModuleKey, module.Id);
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("重新扫描模块", EditorStyles.miniButton))
                    ReloadModules();
                GUILayout.Space(4f);
            }
        }

        private void DrawContent()
        {
            IPulletWorkspaceModule module = _modules.FirstOrDefault(item => item.Id == _selectedModuleId)
                                              ?? _modules[0];
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                GUILayout.Space(10f);
                EditorGUILayout.LabelField(module.DisplayName, EditorStyles.largeLabel);
                if (!string.IsNullOrWhiteSpace(module.Description))
                    EditorGUILayout.LabelField(module.Description, EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(6f);

                _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
                TryInvoke(module, module.OnGUI);
                EditorGUILayout.EndScrollView();
                GUILayout.Space(6f);
            }
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
    }
}
