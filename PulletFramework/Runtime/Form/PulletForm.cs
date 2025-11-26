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
        public static int readCount = 0;
        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }

        /// <summary>
        /// 初始化网络系统
        /// </summary>
        public static void Initalize()
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
                Initalize();
            }
            if (Contains<T>()) return;

            T module = Activator.CreateInstance<T>();
            m_Wrappers.Add(typeof(T));
            readCount++;
        }

        public static IEnumerator IsReadFinish()
        {
            float time = Time.time;
            while (readCount != 0)
            {
                yield return null;
            }

            PLogger.Log("所有表加载完毕:"+(Time.time - time));
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
    }
}
