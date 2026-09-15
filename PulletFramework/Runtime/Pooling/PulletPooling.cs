
using System;
using System.Collections.Generic;
using UnityEngine;
using PulletFramework.Resource;

namespace PulletFramework.Pooling
{
    /// <summary>
    /// 游戏对象池系统
    /// </summary>
    public static class PulletPooling
    {
        private static bool m_IsInitialize = false;
        private static readonly List<Spawner> m_Spawners = new List<Spawner>();
        private static readonly Dictionary<string, Spawner> m_SpawnersByPackage =
            new Dictionary<string, Spawner>(StringComparer.Ordinal);
        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }

        /// <summary>
        /// 初始化游戏对象池系统
        /// </summary>
        public static void Initialize()
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
                m_SpawnersByPackage.Clear();

                m_IsInitialize = false;
                GameObject root = m_GameObject;
                m_GameObject = null;
                if (root != null)
                    GameObject.Destroy(root);
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
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Package name is required.", nameof(packageName));
            if (!m_IsInitialize)
            {
                Initialize();
            }
            if (m_SpawnersByPackage.TryGetValue(packageName, out Spawner existing))
                return existing;

            // 获取资源包
            if (!PulletResources.TryGetPackage(packageName, out IResourcePackage assetPackage))
                throw new Exception($"Not found asset package : {packageName}");

            // 检测资源包初始化状态
            if (assetPackage.Status == EResourcePackageStatus.None || assetPackage.Status == EResourcePackageStatus.Initializing)
                throw new Exception($"Asset package {packageName} not initialize !");
            if (assetPackage.Status == EResourcePackageStatus.Failed)
                throw new Exception($"Asset package {packageName} initialize failed: {assetPackage.Error}");

            Spawner spawner = new Spawner(gameObject, assetPackage);
            m_Spawners.Add(spawner);
            m_SpawnersByPackage.Add(packageName, spawner);
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
                Initialize();
            }
            if (packageName != null
                && m_SpawnersByPackage.TryGetValue(packageName, out Spawner spawner))
                return spawner;

            PLogger.Warning($"Not found spawner : {packageName}");
            return null;
        }

		/// <summary>销毁指定资源包的生成器。卸载资源包前应先调用此方法。</summary>
		public static bool DestroySpawner(string packageName)
		{
			if (!m_IsInitialize)
				return false;
			if (packageName == null
				|| !m_SpawnersByPackage.TryGetValue(packageName, out Spawner spawner))
				return false;
			m_SpawnersByPackage.Remove(packageName);
			m_Spawners.Remove(spawner);
			spawner.Destroy();
			return true;
		}

        /// <summary>
        /// 检测游戏对象生成器是否存在
        /// </summary>
        /// <param name="packageName">资源包名称</param>
        public static bool HasSpawner(string packageName)
        {
            if (!m_IsInitialize)
            {
                Initialize();
            }
            return packageName != null && m_SpawnersByPackage.ContainsKey(packageName);
        }
        #endregion
    }
}
