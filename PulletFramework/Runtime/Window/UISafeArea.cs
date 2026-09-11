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

    /// <summary>
    /// 将指定的全屏拉伸节点限制在安全区内。背景节点不应挂载该组件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UISafeAreaFitter : MonoBehaviour
    {
        [SerializeField]
        private RectTransform m_Target;

        [SerializeField]
        private ESafeAreaEdge m_Edges = ESafeAreaEdge.All;

        private Vector2 m_OriginalAnchorMin;
        private Vector2 m_OriginalAnchorMax;
        private Vector2 m_OriginalOffsetMin;
        private Vector2 m_OriginalOffsetMax;
        private bool m_HasOriginalLayout;

        public RectTransform Target
        {
            get => m_Target;
            set
            {
                m_Target = value;
                CaptureCurrentLayout();
                Apply(UISafeArea.Current, new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)));
            }
        }

        public ESafeAreaEdge Edges
        {
            get => m_Edges;
            set
            {
                m_Edges = value;
                Apply(UISafeArea.Current, new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)));
            }
        }

        private void Awake()
        {
            if (m_Target == null)
                m_Target = transform as RectTransform;
            CaptureCurrentLayout();
        }

        private void OnEnable()
        {
            UISafeArea.Changed += Apply;
            UISafeArea.Refresh(true);
        }

        private void OnDisable()
        {
            UISafeArea.Changed -= Apply;
        }

        [ContextMenu("重新记录当前布局")]
        public void CaptureCurrentLayout()
        {
            if (m_Target == null)
                return;

            m_OriginalAnchorMin = m_Target.anchorMin;
            m_OriginalAnchorMax = m_Target.anchorMax;
            m_OriginalOffsetMin = m_Target.offsetMin;
            m_OriginalOffsetMax = m_Target.offsetMax;
            m_HasOriginalLayout = true;
        }

        [ContextMenu("立即应用安全区")]
        public void ApplyNow()
        {
            Apply(UISafeArea.Current, new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)));
        }

        private void Apply(Rect safeArea, Vector2Int screenSize)
        {
            if (m_Target == null || !m_HasOriginalLayout || screenSize.x <= 0 || screenSize.y <= 0)
                return;

            Vector2 safeMin = new Vector2(safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y);
            Vector2 safeMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);
            Vector2 anchorMin = m_OriginalAnchorMin;
            Vector2 anchorMax = m_OriginalAnchorMax;

            if ((m_Edges & ESafeAreaEdge.Left) != 0)
                anchorMin.x = safeMin.x;
            if ((m_Edges & ESafeAreaEdge.Right) != 0)
                anchorMax.x = safeMax.x;
            if ((m_Edges & ESafeAreaEdge.Bottom) != 0)
                anchorMin.y = safeMin.y;
            if ((m_Edges & ESafeAreaEdge.Top) != 0)
                anchorMax.y = safeMax.y;

            m_Target.anchorMin = anchorMin;
            m_Target.anchorMax = anchorMax;
            m_Target.offsetMin = m_OriginalOffsetMin;
            m_Target.offsetMax = m_OriginalOffsetMax;
        }
    }
}
