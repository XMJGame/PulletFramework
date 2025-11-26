
using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace PulletFramework.Pooling
{
    /// <summary>
    /// 游戏对象池系统
    /// </summary>
    public static class PulletPooling
    {
        private static bool m_IsInitialize = false;
        private static readonly List<Spawner> m_Spawners = new List<Spawner>();
        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }

        /// <summary>
        /// 初始化游戏对象池系统
        /// </summary>
        public static void Initalize()
        {
            if (m_IsInitialize)
                throw new Exception($"{nameof(PulletPooling)} is initialized !");

            if (m_IsInitialize == false)
            {
                // 创建驱动器
                m_IsInitialize = true;
                m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletPooling)}]");
                PLogger.Log($"{nameof(PulletPooling)} initalize !");
            }
        }

        /// <summary>
        /// 更新游戏对象池系统
        /// </summary>
        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (m_IsInitialize)
            {
                foreach (var spawner in m_Spawners)
                {
                    spawner.Update();
                }
            }
        }

        /// <summary>
        /// 销毁游戏对象池系统
        /// </summary>
        public static void Destroy()
        {
            if (m_IsInitialize)
            {
                foreach (var spawner in m_Spawners)
                {
                    spawner.Destroy();
                }
                m_Spawners.Clear();

                m_IsInitialize = false;
                if (gameObject != null)
                    GameObject.Destroy(gameObject);
                PLogger.Log($"{nameof(PulletPooling)} destroy all !");
            }
        }

        #region 外部调用
        /// <summary>
		/// 创建游戏对象生成器
		/// </summary>
		/// <param name="packageName">资源包名称</param>
		public static Spawner CreateSpawner(string packageName)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            // 获取资源包
            var assetPackage = YooAssets.GetPackage(packageName);
            if (assetPackage == null)
                throw new Exception($"Not found asset package : {packageName}");

            // 检测资源包初始化状态
            if (assetPackage.InitializeStatus == EOperationStatus.None)
                throw new Exception($"Asset package {packageName} not initialize !");
            if (assetPackage.InitializeStatus == EOperationStatus.Failed)
                throw new Exception($"Asset package {packageName} initialize failed !");

            if (HasSpawner(packageName))
                return GetSpawner(packageName);

            Spawner spawner = new Spawner(gameObject, assetPackage);
            m_Spawners.Add(spawner);
            return spawner;
        }

        /// <summary>
        /// 获取游戏对象生成器
        /// </summary>
        /// <param name="packageName">资源包名称</param>
        public static Spawner GetSpawner(string packageName)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            foreach (var spawner in m_Spawners)
            {
                if (spawner.packageName == packageName)
                    return spawner;
            }

            PLogger.Warning($"Not found spawner : {packageName}");
            return null;
        }

        /// <summary>
        /// 检测游戏对象生成器是否存在
        /// </summary>
        /// <param name="packageName">资源包名称</param>
        public static bool HasSpawner(string packageName)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            foreach (var spawner in m_Spawners)
            {
                if (spawner.packageName == packageName)
                    return true;
            }
            return false;
        }
        #endregion
    }
}
