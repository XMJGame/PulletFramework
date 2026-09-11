using System;
using System.Collections.Generic;

namespace PulletFramework.Window
{
    public enum EUIToastType
    {
        Info,
        Success,
        Warning,
        Error,
    }

    public readonly struct UIToastMessage
    {
        public string Text { get; }
        public float Duration { get; }
        public EUIToastType Type { get; }

        public UIToastMessage(string text, float duration, EUIToastType type)
        {
            Text = text;
            Duration = duration;
            Type = type;
        }
    }

    /// <summary>
    /// 由业务项目实现具体的 Loading 和 Toast 视觉表现。
    /// </summary>
    public interface IUIFeedbackPresenter
    {
        void SetLoading(bool visible, string message);
        void ShowToast(UIToastMessage message);
    }

    /// <summary>
    /// 与具体预制体解耦的 Loading 引用计数和 Toast 入口。
    /// </summary>
    public static class PulletUIFeedback
    {
        private sealed class LoadingLease : IDisposable
        {
            private long m_Id;

            public LoadingLease(long id)
            {
                m_Id = id;
            }

            public void Dispose()
            {
                if (m_Id == 0)
                    return;
                ReleaseLoading(m_Id);
                m_Id = 0;
            }
        }

        private static readonly Dictionary<long, string> m_LoadingMessages = new Dictionary<long, string>();
        private static readonly List<long> m_LoadingOrder = new List<long>();
        private static IUIFeedbackPresenter m_Presenter;
        private static long m_NextLoadingId;

        public static int LoadingCount => m_LoadingMessages.Count;
        public static bool IsLoading => m_LoadingMessages.Count > 0;

        public static void SetPresenter(IUIFeedbackPresenter presenter)
        {
            m_Presenter = presenter;
            RefreshLoading();
        }

        /// <summary>
        /// 显示 Loading，并返回必须释放的令牌。推荐使用 using。
        /// </summary>
        public static IDisposable ShowLoading(string message = null)
        {
            long id = ++m_NextLoadingId;
            m_LoadingMessages[id] = message;
            m_LoadingOrder.Add(id);
            RefreshLoading();
            return new LoadingLease(id);
        }

        public static void ShowToast(
            string message,
            EUIToastType type = EUIToastType.Info,
            float duration = 2f)
        {
            if (string.IsNullOrEmpty(message))
                return;
            try
            {
                m_Presenter?.ShowToast(new UIToastMessage(message, Math.Max(0f, duration), type));
            }
            catch (Exception exception)
            {
                PLogger.Error($"[UI Toast Presenter Failed] {exception.Message}");
            }
        }

        public static void ClearLoading()
        {
            m_LoadingMessages.Clear();
            m_LoadingOrder.Clear();
            RefreshLoading();
        }

        internal static void Reset()
        {
            ClearLoading();
            m_Presenter = null;
            m_NextLoadingId = 0;
        }

        private static void ReleaseLoading(long id)
        {
            if (!m_LoadingMessages.Remove(id))
                return;
            m_LoadingOrder.Remove(id);
            RefreshLoading();
        }

        private static void RefreshLoading()
        {
            string message = null;
            if (m_LoadingOrder.Count > 0)
            {
                long id = m_LoadingOrder[m_LoadingOrder.Count - 1];
                m_LoadingMessages.TryGetValue(id, out message);
            }

            try
            {
                m_Presenter?.SetLoading(m_LoadingMessages.Count > 0, message);
            }
            catch (Exception exception)
            {
                PLogger.Error($"[UI Loading Presenter Failed] {exception.Message}");
            }
        }
    }
}
