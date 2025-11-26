#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Xu Mingjun(Xinxiang, Henan) All Rights Reserved.
//  作    者：许明俊
//  创建日期：2020
//  功能描述：PulletFramework 框架（别名：小母鸡框架，名字首字母而起）
//
// *********************************************************************
#endregion
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Event
{
	/// <summary>
	/// 事件系统。
	/// </summary>
	public static class PulletEvent
    {
        private class PostWrapper
        {
            public int postFrame;
            public int eventID;
            public IEventMessage message;

            public void OnRelease()
            {
                postFrame = 0;
                eventID = 0;
                message = null;
            }
        }

        private static bool m_IsInitialize = false;
        private static readonly Dictionary<int, LinkedList<Action<IEventMessage>>> m_Listeners = new Dictionary<int, LinkedList<Action<IEventMessage>>>(1000);
        private static readonly List<PostWrapper> m_PostingList = new List<PostWrapper>(1000);
		private static GameObject m_GameObject;
		public static GameObject gameObject { get { return m_GameObject; } }
		public static Transform transform { get { return m_GameObject.transform; } }
		/// <summary>
		/// 初始化事件系统
		/// </summary>
		public static void Initalize()
        {
			if (m_IsInitialize)
			{
				return;
				throw new Exception($"{nameof(PulletEvent)} is initialized !");
			}

            if (m_IsInitialize == false)
            {
                // 创建驱动器
                m_IsInitialize = true;
				m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletEvent)}]");
				PLogger.Log($"{nameof(PulletEvent)} initalize !");
            }
        }

        /// <summary>
		/// 销毁事件系统
		/// </summary>
        public static void Destroy()
        {
            if (m_IsInitialize)
            {
                ClearAll();

                m_IsInitialize = false;
				if (gameObject != null)
					GameObject.Destroy(gameObject);
				PLogger.Log($"{nameof(PulletEvent)} destroy all !");
            }
        }

        /// <summary>
        /// 更新事件系统
        /// </summary>
        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
			if (!m_IsInitialize) return;
            for (int i = m_PostingList.Count - 1; i >= 0; i--)
            {
                var wrapper = m_PostingList[i];
                if (UnityEngine.Time.frameCount > wrapper.postFrame)
                {
                    SendMessage(wrapper.eventID, wrapper.message);
                    m_PostingList.RemoveAt(i);
                }
            }
        }

        /// <summary>
		/// 清空所有监听
		/// </summary>
		public static void ClearAll()
        {
            foreach (int eventId in m_Listeners.Keys)
            {
                m_Listeners[eventId].Clear();
            }
            m_Listeners.Clear();
            m_PostingList.Clear();
        }

		/// <summary>
		/// 添加监听
		/// </summary>
		public static void AddListener<TEvent>(System.Action<IEventMessage> listener) where TEvent : IEventMessage
		{
			System.Type eventType = typeof(TEvent);
			int eventId = eventType.GetHashCode();
			AddListener(eventId, listener);
		}

        /// <summary>
        /// 添加监听
        /// </summary>
        public static void AddListener(System.Type eventType, System.Action<IEventMessage> listener)
        {
            int eventId = eventType.GetHashCode();
            AddListener(eventId, listener);
        }

        /// <summary>
        /// 添加监听
        /// </summary>
        public static void AddListener(int eventId, System.Action<IEventMessage> listener)
		{
			if (!m_IsInitialize) 
			{
				Initalize();
			}
			if (m_Listeners.ContainsKey(eventId) == false)
				m_Listeners.Add(eventId, new LinkedList<Action<IEventMessage>>());
			if (m_Listeners[eventId].Contains(listener) == false)
				m_Listeners[eventId].AddLast(listener);
		}


		/// <summary>
		/// 移除监听
		/// </summary>
		public static void RemoveListener<TEvent>(System.Action<IEventMessage> listener) where TEvent : IEventMessage
		{
			System.Type eventType = typeof(TEvent);
			int eventId = eventType.GetHashCode();
			RemoveListener(eventId, listener);
		}

        /// <summary>
        /// 移除监听
        /// </summary>
        public static void RemoveListener(System.Type eventType, System.Action<IEventMessage> listener)
        {
            int eventId = eventType.GetHashCode();
            RemoveListener(eventId, listener);
        }

        /// <summary>
        /// 移除监听
        /// </summary>
        public static void RemoveListener(int eventId, System.Action<IEventMessage> listener)
		{
			if (!m_IsInitialize)
			{
				Initalize();
			}
			if (m_Listeners.ContainsKey(eventId))
			{
				if (m_Listeners[eventId].Contains(listener))
					m_Listeners[eventId].Remove(listener);
			}
		}


		/// <summary>
		/// 实时广播事件
		/// </summary>
		public static void SendMessage(IEventMessage message)
		{
			int eventId = message.GetType().GetHashCode();
			SendMessage(eventId, message);
		}

		/// <summary>
		/// 实时广播事件
		/// </summary>
		/// <typeparam name="TEvent"></typeparam>
		public static void SendMessage<TEvent>() where TEvent : IEventMessage
		{
			TEvent message = Activator.CreateInstance<TEvent>();
			int eventId = message.GetType().GetHashCode();
			SendMessage(eventId, message);
		}

		/// <summary>
		/// 实时广播事件
		/// </summary>
		public static void SendMessage(int eventId, IEventMessage message)
		{
			if (m_Listeners.ContainsKey(eventId) == false)
				return;

			LinkedList<Action<IEventMessage>> listeners = m_Listeners[eventId];
			if (listeners.Count > 0)
			{
				var currentNode = listeners.Last;
				while (currentNode != null)
				{
					currentNode.Value.Invoke(message);
					currentNode = currentNode.Previous;
				}
			}
		}

		/// <summary>
		/// 延迟广播事件
		/// </summary>
		public static void PostMessage(IEventMessage message)
		{
			int eventId = message.GetType().GetHashCode();
			PostMessage(eventId, message);
		}

		/// <summary>
		/// 延迟广播事件
		/// </summary>
		public static void PostMessage(int eventId, IEventMessage message)
		{
			if (!m_IsInitialize)
			{
				Initalize();
			}
			var wrapper = new PostWrapper();
			wrapper.postFrame = UnityEngine.Time.frameCount;
			wrapper.eventID = eventId;
			wrapper.message = message;
			m_PostingList.Add(wrapper);
		}
	}
}
