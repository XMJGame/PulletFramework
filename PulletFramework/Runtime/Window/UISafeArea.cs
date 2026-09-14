using System;
using UnityEngine;

namespace PulletFramework.Window
{
    /// <summary>
    /// 为安全区提供平台数据。返回值使用物理屏幕像素和左下角原点。
    /// 默认实现读取 Unity 的 Screen.safeArea。
    /// </summary>
    public interface IUISafeAreaProvider
    {
        Rect GetSafeArea();
    }

    internal sealed class UnitySafeAreaProvider : IUISafeAreaProvider
    {
        public Rect GetSafeArea()
        {
            return Screen.safeArea;
        }
    }

    internal sealed class FixedSafeAreaProvider : IUISafeAreaProvider
    {
        private readonly Rect m_SafeArea;

        public FixedSafeAreaProvider(Rect safeArea)
        {
            m_SafeArea = safeArea;
        }

        public Rect GetSafeArea()
        {
            return m_SafeArea;
        }
    }

    /// <summary>
    /// 安全区数据入口。平台 SDK 可通过 SetProvider 注入自己的安全区实现。
    /// </summary>
    public static class UISafeArea
    {
        private static readonly IUISafeAreaProvider m_DefaultProvider = new UnitySafeAreaProvider();
        private static IUISafeAreaProvider m_Provider = m_DefaultProvider;
        private static Rect m_LastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private static int m_LastScreenWidth = -1;
        private static int m_LastScreenHeight = -1;

        public static event Action<Rect, Vector2Int> Changed;

        public static Rect Current => ClampToScreen(m_Provider.GetSafeArea());

        /// <summary>
        /// 将小游戏平台返回的左上角原点、逻辑像素安全区转换为 Unity 物理像素安全区。
        /// </summary>
        public static Rect FromTopLeftLogical(
            float left,
            float top,
            float right,
            float bottom,
            float logicalWidth,
            float logicalHeight)
        {
            if (logicalWidth <= 0f || logicalHeight <= 0f || right <= left || bottom <= top)
                return new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));

            float scaleX = Mathf.Max(1f, Screen.width) / logicalWidth;
            float scaleY = Mathf.Max(1f, Screen.height) / logicalHeight;
            return Rect.MinMaxRect(
                left * scaleX,
                (logicalHeight - bottom) * scaleY,
                right * scaleX,
                (logicalHeight - top) * scaleY);
        }

        public static void SetProvider(IUISafeAreaProvider provider)
        {
            m_Provider = provider ?? m_DefaultProvider;
            Refresh(true);
        }

        public static void ResetProvider()
        {
            SetProvider(m_DefaultProvider);
        }

        public static void SetOverride(Rect safeArea)
        {
            SetProvider(new FixedSafeAreaProvider(safeArea));
        }

        public static void Refresh(bool force = false)
        {
            Rect safeArea = Current;
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (!force && width == m_LastScreenWidth && height == m_LastScreenHeight && safeArea == m_LastSafeArea)
                return;

            m_LastScreenWidth = width;
            m_LastScreenHeight = height;
            m_LastSafeArea = safeArea;
            Changed?.Invoke(safeArea, new Vector2Int(width, height));
        }

        internal static void Update()
        {
            Refresh();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            m_Provider = m_DefaultProvider;
            m_LastSafeArea = new Rect(-1f, -1f, -1f, -1f);
            m_LastScreenWidth = -1;
            m_LastScreenHeight = -1;
            Changed = null;
        }

        private static Rect ClampToScreen(Rect safeArea)
        {
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            if (safeArea.xMax <= safeArea.xMin || safeArea.yMax <= safeArea.yMin)
                return new Rect(0f, 0f, width, height);
            float xMin = Mathf.Clamp(safeArea.xMin, 0f, width);
            float yMin = Mathf.Clamp(safeArea.yMin, 0f, height);
            float xMax = Mathf.Clamp(safeArea.xMax, xMin, width);
            float yMax = Mathf.Clamp(safeArea.yMax, yMin, height);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }

    [Flags]
    public enum ESafeAreaEdge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        All = Left | Right | Top | Bottom,
    }

}
