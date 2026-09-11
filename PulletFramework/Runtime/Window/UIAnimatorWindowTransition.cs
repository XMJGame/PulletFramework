using System;
using System.Collections;
using UnityEngine;

namespace PulletFramework.Window
{
    /// <summary>
    /// 使用 Animator 状态播放窗口进出场动画。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class UIAnimatorWindowTransition : MonoBehaviour, IUIWindowTransition
    {
        [SerializeField]
        private Animator m_Animator;

        [SerializeField]
        private int m_LayerIndex = 0;

        [SerializeField]
        private string m_EnterState = "Base Layer.Open";

        [SerializeField]
        private string m_ExitState = "Base Layer.Close";

        [SerializeField, Min(0.1f)]
        private float m_Timeout = 5f;

        [SerializeField]
        private bool m_IgnoreTimeScale = true;

        private Coroutine m_Coroutine;
        private Action m_Completed;
        private string m_CurrentState;

        public bool IsPlaying => m_Coroutine != null;

        private void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponent<Animator>();
        }

        public void PlayEnter(Action completed)
        {
            Play(m_EnterState, completed);
        }

        public void PlayExit(Action completed)
        {
            Play(m_ExitState, completed);
        }

        public void CompleteImmediately()
        {
            if (m_Coroutine != null)
                StopCoroutine(m_Coroutine);
            m_Coroutine = null;

            if (m_Animator != null && !string.IsNullOrEmpty(m_CurrentState))
            {
                m_Animator.Play(m_CurrentState, m_LayerIndex, 1f);
                m_Animator.Update(0f);
            }
            Complete();
        }

        public void Cancel()
        {
            if (m_Coroutine != null)
                StopCoroutine(m_Coroutine);
            m_Coroutine = null;
            m_Completed = null;
            m_CurrentState = null;
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void Play(string stateName, Action completed)
        {
            Cancel();
            m_Completed = completed;
            m_CurrentState = stateName;

            if (m_Animator == null)
                m_Animator = GetComponent<Animator>();
            if (m_Animator == null || m_Animator.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
            {
                Complete();
                return;
            }

            m_Animator.Play(stateName, m_LayerIndex, 0f);
            m_Animator.Update(0f);
            m_Coroutine = StartCoroutine(WaitForState(stateName));
        }

        private IEnumerator WaitForState(string stateName)
        {
            float elapsed = 0f;
            while (elapsed < m_Timeout)
            {
                AnimatorStateInfo state = m_Animator.GetCurrentAnimatorStateInfo(m_LayerIndex);
                if (state.IsName(stateName)
                    && !m_Animator.IsInTransition(m_LayerIndex)
                    && state.normalizedTime >= 1f)
                {
                    Complete();
                    yield break;
                }

                elapsed += m_IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }

            PLogger.Warning($"Animator transition timed out: {name}/{stateName}");
            Complete();
        }

        private void Complete()
        {
            m_Coroutine = null;
            m_CurrentState = null;
            Action completed = m_Completed;
            m_Completed = null;
            completed?.Invoke();
        }
    }
}
