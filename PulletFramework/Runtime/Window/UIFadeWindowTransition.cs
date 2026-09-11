using System;
using System.Collections;
using UnityEngine;

namespace PulletFramework.Window
{
    /// <summary>
    /// 可选的窗口淡入淡出组件。挂到窗口预制体根节点即可启用。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIFadeWindowTransition : MonoBehaviour, IUIWindowTransition
    {
        [SerializeField, Min(0f)]
        private float m_EnterDuration = 0.18f;

        [SerializeField, Min(0f)]
        private float m_ExitDuration = 0.14f;

        [SerializeField]
        private bool m_IgnoreTimeScale = true;

        [SerializeField]
        private AnimationCurve m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private CanvasGroup m_CanvasGroup;
        private Coroutine m_Coroutine;
        private Action m_Completed;
        private float m_TargetAlpha = 1f;

        public bool IsPlaying => m_Coroutine != null;

        private void Awake()
        {
            m_CanvasGroup = GetComponent<CanvasGroup>();
        }

        public void PlayEnter(Action completed)
        {
            Play(0f, 1f, m_EnterDuration, completed);
        }

        public void PlayExit(Action completed)
        {
            float from = m_CanvasGroup != null ? m_CanvasGroup.alpha : 1f;
            Play(from, 0f, m_ExitDuration, completed);
        }

        public void Cancel()
        {
            if (m_Coroutine != null)
                StopCoroutine(m_Coroutine);
            m_Coroutine = null;
            m_Completed = null;
        }

        public void CompleteImmediately()
        {
            if (m_Coroutine != null)
                StopCoroutine(m_Coroutine);
            m_Coroutine = null;
            if (m_CanvasGroup != null)
                m_CanvasGroup.alpha = m_TargetAlpha;
            Complete();
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void Play(float from, float to, float duration, Action completed)
        {
            Cancel();
            m_Completed = completed;
            m_TargetAlpha = to;
            if (m_CanvasGroup == null)
                m_CanvasGroup = GetComponent<CanvasGroup>();

            if (duration <= 0f)
            {
                m_CanvasGroup.alpha = to;
                Complete();
                return;
            }

            m_CanvasGroup.alpha = from;
            m_Coroutine = StartCoroutine(PlayRoutine(from, to, duration));
        }

        private IEnumerator PlayRoutine(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += m_IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float curvedProgress = m_Curve == null ? progress : m_Curve.Evaluate(progress);
                m_CanvasGroup.alpha = Mathf.LerpUnclamped(from, to, curvedProgress);
                yield return null;
            }

            m_CanvasGroup.alpha = to;
            Complete();
        }

        private void Complete()
        {
            m_Coroutine = null;
            Action completed = m_Completed;
            m_Completed = null;
            completed?.Invoke();
        }
    }
}
