using UnityEngine;

namespace PulletFramework.Window
{
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
