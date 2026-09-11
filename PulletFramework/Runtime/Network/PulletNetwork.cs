#region Copyright (C)
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架 - 网络框架
//
// *********************************************************************
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Network
{
    /// <summary>
    /// 网络相关
    /// </summary>
    public static class PulletNetwork
    {
        private static bool m_IsInitialize = false;
        private readonly static List<TcpClient> m_TcpClients = new List<TcpClient>();
        private readonly static List<TcpClient> m_PendingTcpRemovals = new List<TcpClient>();
        private readonly static List<PulletWebSocketClient> m_WebSocketClients = new List<PulletWebSocketClient>();
        private readonly static List<PulletWebSocketClient> m_PendingWebSocketRemovals =
            new List<PulletWebSocketClient>();
        private static IWebSocketTransportFactory m_WebSocketTransportFactory =
            new DotNetWebSocketTransportFactory();
        private static bool m_IsUpdating;
        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }
        /// <summary>
        /// 初始化网络系统
        /// </summary>
        public static void Initialize()
        {
            if (m_IsInitialize)
                throw new Exception($"{nameof(PulletNetwork)} is initialized !");

            if (m_IsInitialize == false)
            {
                // 创建驱动器
                m_IsInitialize = true;
                m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletNetwork)}]");
                PLogger.Log($"{nameof(PulletNetwork)} initalize !");
            }
        }

        internal static void EnsureInitialized()
        {
            if (!m_IsInitialize)
                Initialize();
        }

        /// <summary>
        /// 销毁网络系统
        /// </summary>
        public static void Destroy()
        {
            if (m_IsInitialize)
            {
                foreach (var client in m_TcpClients)
                {
                    client.Destroy();
                }
                m_TcpClients.Clear();
                m_PendingTcpRemovals.Clear();

                foreach (PulletWebSocketClient client in m_WebSocketClients)
                    client.Dispose();
                m_WebSocketClients.Clear();
                m_PendingWebSocketRemovals.Clear();
                PulletHttp.Destroy();

                m_IsInitialize = false;
                if (m_GameObject != null)
                    GameObject.Destroy(m_GameObject);
                PLogger.Log($"{nameof(PulletNetwork)} destroy all !");
            }
        }

        /// <summary>
        /// 更新网络系统
        /// </summary>
        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (m_IsInitialize)
            {
                m_IsUpdating = true;
                try
                {
                    int tcpCount = m_TcpClients.Count;
                    for (int i = 0; i < tcpCount && i < m_TcpClients.Count; i++)
                        m_TcpClients[i].Update(deltaTime, unscaledDeltaTime);

                    if (!m_IsInitialize)
                        return;

                    int webSocketCount = m_WebSocketClients.Count;
                    for (int i = 0; i < webSocketCount && i < m_WebSocketClients.Count; i++)
                        m_WebSocketClients[i].Update(unscaledDeltaTime);

                    if (!m_IsInitialize)
                        return;
                    PulletHttp.Update(unscaledDeltaTime);
                }
                finally
                {
                    m_IsUpdating = false;
                    FlushPendingRemovals();
                }
            }
        }

        /// <summary>
        /// 创建TCP客户端
        /// </summary>
        /// <param name="packageBodyMaxSize">网络包体最大长度</param>
        public static TcpClient CreateTcpClient(IChannelHelper channelHelper = null)
        {
            if (m_IsInitialize == false)
                Initialize();

            var client = new TcpClient(channelHelper);
            m_TcpClients.Add(client);
            return client;
        }

        /// <summary>
        /// 销毁TCP客户端
        /// </summary>
        public static void DestroyTcpClient(TcpClient client)
        {
            if (client == null)
                return;

            client.Dispose();
            if (m_IsUpdating)
            {
                if (!m_PendingTcpRemovals.Contains(client))
                    m_PendingTcpRemovals.Add(client);
            }
            else
            {
                m_TcpClients.Remove(client);
            }
        }

        /// <summary>
        /// 注册 WebSocket 底层实现。微信、抖音小游戏应在创建客户端前注入平台 Provider。
        /// </summary>
        public static void SetWebSocketTransportFactory(IWebSocketTransportFactory factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            if (m_WebSocketClients.Count > 0)
                throw new InvalidOperationException("Cannot replace WebSocket transport while clients exist.");
            m_WebSocketTransportFactory = factory;
        }

        public static PulletWebSocketClient CreateWebSocketClient(PulletWebSocketOptions options)
        {
            EnsureInitialized();
            var client = new PulletWebSocketClient(options, m_WebSocketTransportFactory);
            m_WebSocketClients.Add(client);
            return client;
        }

        public static void DestroyWebSocketClient(PulletWebSocketClient client)
        {
            if (client == null)
                return;
            client.Dispose();
            if (m_IsUpdating)
            {
                if (!m_PendingWebSocketRemovals.Contains(client))
                    m_PendingWebSocketRemovals.Add(client);
            }
            else
            {
                m_WebSocketClients.Remove(client);
            }
        }

        private static void FlushPendingRemovals()
        {
            for (int i = 0; i < m_PendingTcpRemovals.Count; i++)
                m_TcpClients.Remove(m_PendingTcpRemovals[i]);
            m_PendingTcpRemovals.Clear();

            for (int i = 0; i < m_PendingWebSocketRemovals.Count; i++)
                m_WebSocketClients.Remove(m_PendingWebSocketRemovals[i]);
            m_PendingWebSocketRemovals.Clear();
        }
    }
}
