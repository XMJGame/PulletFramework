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
    /// 主线程事件派发。监听逆序执行；回调中新增监听不参与当前Send，退订与清空立即生效。
    /// Post最早在下一帧执行，回调异常只记录，不自动重试事件。
    /// </summary>
    public static class PulletEvent
    {
        private sealed class Listener
        {
            public Action<IEventMessage> Callback;
            public bool Active = true;
        }

        private sealed class ListenerSet
        {
            public readonly List<Listener> Items = new List<Listener>();
            private Listener[] snapshot;
            public Listener[] Snapshot => snapshot ?? (snapshot = Items.ToArray());
            public void Invalidate() => snapshot = null;
        }

        private sealed class PostWrapper
        {
            public int Frame;
            public int EventId;
            public IEventMessage Message;
        }

        private static bool m_IsInitialize;
        private static readonly Dictionary<int, ListenerSet> m_Listeners = new Dictionary<int, ListenerSet>(1000);
        private static readonly List<PostWrapper> m_PostingList = new List<PostWrapper>(1000);
        // 本帧已接纳的批次由Update独占，ClearAll通过版本作废，不能在用户回调中改动其下标。
        private static readonly List<PostWrapper> m_Ready = new List<PostWrapper>();
        private static bool m_IsUpdating;
        private static long m_Version;
        private static GameObject m_GameObject;
        public static GameObject gameObject => m_GameObject;
        public static Transform transform => m_GameObject.transform;

        public static void Initialize()
        {
            if (m_IsInitialize) return;
            m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletEvent)}]");
            m_IsInitialize = true;
            PLogger.Log($"{nameof(PulletEvent)} initialized.");
        }

        public static void Destroy()
        {
            if (!m_IsInitialize) return;
            m_IsInitialize = false;
            ClearAll();
            var root = m_GameObject;
            m_GameObject = null;
            if (root != null) GameObject.Destroy(root);
        }

        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (!m_IsInitialize || m_IsUpdating) return;
            m_IsUpdating = true;
            long version = m_Version;
            try
            {
                // 先从等待队列移交，再进入外部回调；保留原来的后投递先执行顺序。
                int retained = 0;
                for (int i = 0; i < m_PostingList.Count; i++)
                {
                    var post = m_PostingList[i];
                    if (Time.frameCount > post.Frame) m_Ready.Add(post);
                    else m_PostingList[retained++] = post;
                }
                // 一次压缩待处理队列，避免逐项删除混合批次时反复搬移元素。
                m_PostingList.RemoveRange(retained, m_PostingList.Count - retained);
                for (int i = m_Ready.Count - 1; i >= 0; i--)
                {
                    if (version != m_Version || !m_IsInitialize) break;
                    var post = m_Ready[i];
                    SendMessage(post.EventId, post.Message);
                }
            }
            finally { m_Ready.Clear(); m_IsUpdating = false; }
        }

        /// <summary>移除全部监听与待投递事件，同时作废当前批次尚未交付的事件。</summary>
        public static void ClearAll()
        {
            foreach (var set in m_Listeners.Values)
                foreach (var listener in set.Items) listener.Active = false;
            m_Listeners.Clear();
            m_PostingList.Clear();
            m_Version++;
        }

        public static void AddListener<TEvent>(Action<IEventMessage> listener) where TEvent : IEventMessage
            => AddListener(typeof(TEvent), listener);
        public static void AddListener(Type eventType, Action<IEventMessage> listener)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            AddListener(eventType.GetHashCode(), listener);
        }
        public static void AddListener(int eventId, Action<IEventMessage> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (!m_IsInitialize) Initialize();
            if (!m_Listeners.TryGetValue(eventId, out var set))
                m_Listeners.Add(eventId, set = new ListenerSet());
            foreach (var item in set.Items) if (item.Callback == listener) return;
            set.Items.Add(new Listener { Callback = listener });
            set.Invalidate();
        }

        public static void RemoveListener<TEvent>(Action<IEventMessage> listener) where TEvent : IEventMessage
            => RemoveListener(typeof(TEvent), listener);
        public static void RemoveListener(Type eventType, Action<IEventMessage> listener)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            RemoveListener(eventType.GetHashCode(), listener);
        }
        public static void RemoveListener(int eventId, Action<IEventMessage> listener)
        {
            if (!m_IsInitialize || !m_Listeners.TryGetValue(eventId, out var set)) return;
            for (int i = 0; i < set.Items.Count; i++)
            {
                var item = set.Items[i];
                if (item.Callback != listener) continue;
                item.Active = false;
                set.Items.RemoveAt(i);
                set.Invalidate();
                if (set.Items.Count == 0) m_Listeners.Remove(eventId);
                return;
            }
        }

        public static void SendMessage(IEventMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            SendMessage(message.GetType().GetHashCode(), message);
        }
        public static void SendMessage<TEvent>() where TEvent : IEventMessage
            => SendMessage(Activator.CreateInstance<TEvent>());

        public static void SendMessage(int eventId, IEventMessage message)
        {
            if (!m_Listeners.TryGetValue(eventId, out var set)) return;
            var snapshot = set.Snapshot;
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                var listener = snapshot[i];
                if (!listener.Active) continue;
                try { listener.Callback(message); }
                catch (Exception error) { PLogger.Exception(error, $"Event listener failed: {eventId}"); }
            }
        }

        public static void PostMessage(IEventMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            PostMessage(message.GetType().GetHashCode(), message);
        }
        public static void PostMessage(int eventId, IEventMessage message)
        {
            if (!m_IsInitialize) Initialize();
            m_PostingList.Add(new PostWrapper { Frame = Time.frameCount, EventId = eventId, Message = message });
        }
    }
}
