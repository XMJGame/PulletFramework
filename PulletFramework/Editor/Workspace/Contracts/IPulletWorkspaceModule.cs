namespace PulletFramework.Editor.Workspace
{
    /// <summary>
    /// 编辑器模块通过此契约向统一工作台注册页面。
    /// 模块仅依赖契约程序集，工作台会在编辑器加载时自动发现实现。
    /// </summary>
    public interface IPulletWorkspaceModule
    {
        string Id { get; }
        string DisplayName { get; }
        string Description { get; }
        int Order { get; }

        void OnEnable();
        void OnDisable();
        void OnGUI();
    }

    /// <summary>使用 UI Toolkit 构建内容的 Workspace 模块可选契约。</summary>
    public interface IPulletWorkspaceVisualModule
    {
        void CreateGUI(UnityEngine.UIElements.VisualElement root);
    }
}
