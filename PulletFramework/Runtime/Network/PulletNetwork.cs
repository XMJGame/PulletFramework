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
        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }
        /// <summary>
        /// 初始化网络系统
        /// </summary>
        public static void Initalize()
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
                foreach (var client in m_TcpClients)
                {
                    client.Update(deltaTime, unscaledDeltaTime);
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
                Initalize();//throw new Exception($"{nameof(PulletNetwork)} not initialized !");

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
            m_TcpClients.Remove(client);
        }
    }
}
