using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Form
{
    /// <summary>
    /// 表格读取管理
    /// </summary>
    public static class PulletForm
    {
        private static bool m_IsInitialize = false;
        private static readonly List<Type> m_Wrappers = new List<Type>(100);
        private static readonly List<Action> m_Resetters = new List<Action>(100);
        private static readonly Dictionary<Type, Action> m_RetryActions =
            new Dictionary<Type, Action>();
        private static readonly Dictionary<Type, FormLoadFailure> m_Failures =
            new Dictionary<Type, FormLoadFailure>();
        private static int m_ReadGeneration;
        public static int readCount { get; private set; }
        public static bool IsReadComplete => m_IsInitialize && readCount == 0;
        public static bool IsReadSuccessful => IsReadComplete && m_Failures.Count == 0;
        public static IReadOnlyList<FormLoadFailure> Failures =>
            new List<FormLoadFailure>(m_Failures.Values);
        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }

        /// <summary>
        /// 初始化网络系统
        /// </summary>
        public static void Initialize()
        {
            if (m_IsInitialize)
                throw new Exception($"{nameof(PulletForm)} is initialized !");

            if (m_IsInitialize == false)
            {
                // 创建驱动器
                m_IsInitialize = true;
                m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletForm)}]");
                PLogger.Log($"{nameof(PulletForm)} initalize !");
            }
        }

        /// <summary>
        /// 销毁表格系统
        /// </summary>
        public static void Destroy()
        {
            if (m_IsInitialize)
            {
                m_ReadGeneration++;
                readCount = 0;
                for (int i = 0; i < m_Resetters.Count; i++)
                    m_Resetters[i]?.Invoke();
                m_Resetters.Clear();
                m_RetryActions.Clear();
                m_Failures.Clear();
                m_Wrappers.Clear();
                m_IsInitialize = false;
                if (gameObject != null)
                    GameObject.Destroy(gameObject);
                PLogger.Log($"{nameof(PulletForm)} destroy all !");
            }
        }

        /// <summary>
        /// 添加表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void AddForm<T>() where T : class
        {
            if (!m_IsInitialize)
            {
                Initialize();
            }
            if (Contains<T>()) return;

            Activator.CreateInstance<T>();
            m_Wrappers.Add(typeof(T));
        }

        public static IEnumerator IsReadFinish()
        {
            float time = Time.realtimeSinceStartup;
            while (readCount != 0)
            {
                yield return null;
            }

            PLogger.Log("所有表加载完毕:" + (Time.realtimeSinceStartup - time));
        }

        /// <summary>重新加载当前批次中失败的表；正在加载的表不会重复启动。</summary>
        public static int RetryFailedForms()
        {
            if (!m_IsInitialize || m_Failures.Count == 0)
                return 0;

            var retries = new List<Action>(m_Failures.Count);
            foreach (Type type in m_Failures.Keys)
            {
                if (m_RetryActions.TryGetValue(type, out Action retry) && retry != null)
                    retries.Add(retry);
            }

            int started = 0;
            for (int i = 0; i < retries.Count; i++)
            {
                retries[i]();
                started++;
            }
            return started;
        }

        /// <summary>
        /// 查询单例是否存在
        /// </summary>
        public static bool Contains<T>() where T : class
        {
            System.Type type = typeof(T);
            for (int i = 0; i < m_Wrappers.Count; i++)
            {
                if (m_Wrappers[i] == type)
                    return true;
            }
            return false;
        }

        internal static int BeginRead(Type formType, string path, Action resetter, Action retry)
        {
            if (!m_IsInitialize)
                Initialize();
            if (resetter != null && !m_Resetters.Contains(resetter))
                m_Resetters.Add(resetter);
            if (formType != null)
            {
                m_Failures.Remove(formType);
                if (retry != null)
                    m_RetryActions[formType] = retry;
            }
            readCount++;
            return m_ReadGeneration;
        }

        internal static bool IsCurrentRead(int generation)
        {
            return m_IsInitialize && generation == m_ReadGeneration;
        }

        internal static void CompleteRead(
            int generation, Type formType, string path, string error)
        {
            if (!IsCurrentRead(generation))
                return;
            if (formType != null)
            {
                if (string.IsNullOrEmpty(error))
                    m_Failures.Remove(formType);
                else
                    m_Failures[formType] = new FormLoadFailure(formType, path, error);
            }
            readCount = Math.Max(0, readCount - 1);
        }
    }

    public sealed class FormLoadFailure
    {
        public Type FormType { get; }
        public string Path { get; }
        public string Error { get; }

        public FormLoadFailure(Type formType, string path, string error)
        {
            FormType = formType;
            Path = path;
            Error = error;
        }
    }
}
