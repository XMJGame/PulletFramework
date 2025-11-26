using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PulletFramework.Network
{
    public static class MessageDistribute
    {
        private static bool m_IsInitialize = false;
        private static Dictionary<int, List<NetMessageHandler>> mNetMessageHandlerDict = new Dictionary<int, List<NetMessageHandler>>();
        public static void Initalize()
        {
            if (!m_IsInitialize)
            {
                m_IsInitialize = true;
                Assembly assembly = Assembly.GetExecutingAssembly();
                AddAssembly(assembly);
            }
        }

        public static void AddAssembly(Assembly assembly)
        {
            Type packetHandlerBaseType = typeof(NetMessageHandler);
            Type[] types = assembly.GetTypes();

            for (int i = 0; i < types.Length; i++)
            {
                if (!types[i].IsClass || types[i].IsAbstract)
                {
                    continue;
                }
                if (types[i].BaseType == packetHandlerBaseType)
                {
                    NetMessageHandler packetHandler = (NetMessageHandler)Activator.CreateInstance(types[i]);

                    List<NetMessageHandler> packetHandlerBaseTypes;
                    if (mNetMessageHandlerDict.TryGetValue(packetHandler.Id, out packetHandlerBaseTypes))
                    {
                        if (!packetHandlerBaseTypes.Contains(packetHandler))
                            packetHandlerBaseTypes.Add(packetHandler);
                    }
                    else
                    {
                        packetHandlerBaseTypes = new List<NetMessageHandler>();
                        packetHandlerBaseTypes.Add(packetHandler);
                        mNetMessageHandlerDict.Add(packetHandler.Id, packetHandlerBaseTypes);
                    }
                }
            }
        }

        public static void AddNetMessageHandler(int id, NetMessageHandler packageHandler)
        {
            List<NetMessageHandler> packetHandlerBaseTypes;
            if (mNetMessageHandlerDict.TryGetValue(packageHandler.Id, out packetHandlerBaseTypes))
            {
                if (!packetHandlerBaseTypes.Contains(packageHandler))
                    packetHandlerBaseTypes.Add(packageHandler);
                else
                    PLogger.Error("重复绑定");
            }
            else
            {
                packetHandlerBaseTypes = new List<NetMessageHandler>();
                packetHandlerBaseTypes.Add(packageHandler);
                mNetMessageHandlerDict.Add(packageHandler.Id, packetHandlerBaseTypes);
            }
        }

        /// <summary>
        /// 消息分发
        /// </summary>
        /// <param name="packet"></param>
        internal static void MessageDispose(TcpChannel tcpChannel, NetPackage packet)
        {
            if (!m_IsInitialize)
            {
                Initalize();
            }
            if (packet.bodyBytes != null)
                PLogger.DebugLog($"接收到消息:{packet.msgId}，byteLength:{packet.bodyBytes.Length}");
            else
                PLogger.DebugLog($"接收到消息:{packet.msgId}");
            
            List<NetMessageHandler> packetHandlerBaseTypes;
            if (mNetMessageHandlerDict.TryGetValue(packet.msgId, out packetHandlerBaseTypes))
            {
                for (int i = 0; i < packetHandlerBaseTypes.Count; i++)
                {
                    if (packet.bodyBytes == null)
                    {
                        packetHandlerBaseTypes[i].Handle(null);
                    }
                    else
                    {
                        Type type = packetHandlerBaseTypes[i].GetMessageType();
                        if (type != null)
                        {
                            try
                            {
                                object message = tcpChannel.channelHelper.DeSerializeMessage(type, packet.bodyBytes);
                                packetHandlerBaseTypes[i].Handle(message);
                            }
                            catch (Exception e)
                            {
                                PLogger.Error($"msgId：{packet.msgId} Message DeSerialize error:{e.Message}");
                            }

                        }
                        else
                        {
                            packetHandlerBaseTypes[i].Handle(null);
                        }
                    }
                }
            }
            else
            {
                if (packet.bodyBytes != null)
                {
                    PLogger.Log($"消息:{packet.msgId} 没有继承 NetMessageHandler的类。");
                }
            }
        }
    }
}
