using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Singleton
{
    public static class PulletSingleton
	{
		private class Wrapper
		{
			public int Priority { private set; get; }
			public ISingleton Singleton { private set; get; }

			public Wrapper(ISingleton module, int priority)
			{
				Singleton = module;
				Priority = priority;
			}
		}

		private static bool m_IsInitialize = false;
		private static readonly List<Wrapper>m_Wrappers = new List<Wrapper>(100);
		private static GameObject m_GameObject;
		public static GameObject gameObject { get { return m_GameObject; } }
		public static Transform transform { get { return m_GameObject.transform; } }

		private static MonoBehaviour m_Mono;
		public static MonoBehaviour mono { get { return m_Mono; } }
		private static bool m_IsDirty = false;

		/// <summary>
		/// 初始化单例系统
		/// </summary>
		public static void Initialize()
		{
			if (m_IsInitialize)
				throw new Exception($"{nameof(PulletSingleton)} is initialized !");

			if (m_IsInitialize == false)
			{
				m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletSingleton)}]");
				m_Mono = gameObject.AddComponent<PulletSingletonMono>();
				// 创建驱动器
				m_IsInitialize = true;
				PLogger.Log($"{nameof(PulletSingleton)} initalize !");
			}
		}

		/// <summary>
		/// 销毁单例系统
		/// </summary>
		internal static void Destroy()
		{
			if (m_IsInitialize)
			{
				DestroyAll();

				m_IsInitialize = false;
				if (gameObject != null)
					GameObject.Destroy(gameObject);
				PLogger.Log($"{nameof(PulletSingleton)} destroy all !");
			}
		}

		/// <summary>
		/// 更新单例系统
		/// </summary>
		internal static void Update()
		{
			// 如果需要重新排序
			if (m_IsDirty)
			{
				m_IsDirty = false;
				m_Wrappers.Sort((left, right) =>
				{
					if (left.Priority > right.Priority)
						return -1;
					else if (left.Priority == right.Priority)
						return 0;
					else
						return 1;
				});
			}

			// 轮询所有模块
			for (int i = 0; i < m_Wrappers.Count; i++)
			{
				m_Wrappers[i].Singleton.OnUpdate();
			}
		}

		/// <summary>
		/// 获取单例
		/// </summary>
		public static T GetSingleton<T>() where T : class, ISingleton
		{
			System.Type type = typeof(T);
			for (int i = 0; i < m_Wrappers.Count; i++)
			{
				if (m_Wrappers[i].Singleton.GetType() == type)
					return m_Wrappers[i].Singleton as T;
			}

			PLogger.Error($"Not found manager : {type}");
			return null;
		}

		/// <summary>
		/// 查询单例是否存在
		/// </summary>
		public static bool Contains<T>() where T : class, ISingleton
		{
			System.Type type = typeof(T);
			for (int i = 0; i < m_Wrappers.Count; i++)
			{
				if (m_Wrappers[i].Singleton.GetType() == type)
					return true;
			}
			return false;
		}

		/// <summary>
		/// 创建单例
		/// </summary>
		/// <param name="priority">运行时的优先级，优先级越大越早执行。如果没有设置优先级，那么会按照添加顺序执行</param>
		public static T CreateSingleton<T>(int priority = 0) where T : class, ISingleton
		{
			return CreateSingleton<T>(null, priority);
		}

		/// <summary>
		/// 创建单例
		/// </summary>
		/// <param name="createParam">附加参数</param>
		/// <param name="priority">运行时的优先级，优先级越大越早执行。如果没有设置优先级，那么会按照添加顺序执行</param>
		public static T CreateSingleton<T>(System.Object createParam, int priority = 0) where T : class, ISingleton
		{
			if (!m_IsInitialize) 
			{
				Initialize();
			}
			if (priority < 0)
				throw new Exception("The priority can not be negative");

			if (Contains<T>())
				return GetSingleton<T>();
				//throw new Exception($"Module is already existed : {typeof(T)}");

			// 如果没有设置优先级
			if (priority == 0)
			{
				int minPriority = GetMinPriority();
				priority = --minPriority;
			}

			T module = Activator.CreateInstance<T>();
			Wrapper wrapper = new Wrapper(module, priority);
			wrapper.Singleton.OnCreate(createParam);
			m_Wrappers.Add(wrapper);
			m_IsDirty = true;
			return module;
		}

		/// <summary>
		/// 销毁单例
		/// </summary>
		public static bool DestroySingleton<T>() where T : class, ISingleton
		{
			var type = typeof(T);
			for (int i = 0; i < m_Wrappers.Count; i++)
			{
				if (m_Wrappers[i].Singleton.GetType() == type)
				{
					m_Wrappers[i].Singleton.OnDestroy();
					m_Wrappers.RemoveAt(i);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// 开启一个协程
		/// </summary>
		public static Coroutine StartCoroutine(IEnumerator coroutine)
		{
			if (!m_IsInitialize)
			{
				Initialize();
			}
			return m_Mono.StartCoroutine(coroutine);
		}
		public static Coroutine StartCoroutine(string methodName)
		{
			if (!m_IsInitialize)
			{
				Initialize();
			}
			return m_Mono.StartCoroutine(methodName);
		}

		/// <summary>
		/// 停止一个协程
		/// </summary>
		public static void StopCoroutine(Coroutine coroutine)
		{
			if (!m_IsInitialize)
			{
				Initialize();
			}
			m_Mono.StopCoroutine(coroutine);
		}
		public static void StopCoroutine(string methodName)
		{
			if (!m_IsInitialize)
			{
				Initialize();
			}
			m_Mono.StopCoroutine(methodName);
		}

		/// <summary>
		/// 停止所有协程
		/// </summary>
		public static void StopAllCoroutines()
		{
			if (!m_IsInitialize)
			{
				Initialize();
			}
			m_Mono.StopAllCoroutines();
		}

		private static int GetMinPriority()
		{
			int minPriority = 0;
			for (int i = 0; i < m_Wrappers.Count; i++)
			{
				if (m_Wrappers[i].Priority < minPriority)
					minPriority = m_Wrappers[i].Priority;
			}
			return minPriority; //小于等于零
		}
		private static void DestroyAll()
		{
			for (int i = 0; i < m_Wrappers.Count; i++)
			{
				m_Wrappers[i].Singleton.OnDestroy();
			}
			m_Wrappers.Clear();
		}
    }
}
