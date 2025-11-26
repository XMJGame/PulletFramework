using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace PulletFramework.Network
{
    public class DefaultIChannelHelper : IChannelHelper
    {
        private MemoryStream mCachedStream = new MemoryStream(1024 * 8);

        /// <summary>
        /// 获取消息包头长度。
        /// </summary>
        public int PackageHeaderLength => 8;

        /// <summary>
        /// 心跳间隔 单位秒
        /// </summary>
        public int HeartBeatInterval => 10;
        public void RigistHandleErrorCallback(HandleErrorDelegate callback)
        {
           // throw new System.NotImplementedException();
        }

        public NetPackage GetHeartBeat()
        {
            NetPackage package = new NetPackage();
            package.msgId = 2;
            return package;
        }

        public bool Serialize<T>(T packet, Stream destination) where T : NetPackage
        {
            //消息长度
            int packetLength = packet.bodyBytes == null ? 0 : packet.bodyBytes.Length;
            byte[] packetLengthBytes = BitConverter.GetBytes(packetLength);
            ////消息id
            byte[] idBytes = BitConverter.GetBytes(packet.msgId);

            mCachedStream.Seek(0, SeekOrigin.Begin);
            mCachedStream.SetLength(0);
            mCachedStream.Write(packetLengthBytes, 0, packetLengthBytes.Length);
            mCachedStream.Write(idBytes, 0, idBytes.Length);
            if (packet.bodyBytes != null)
                mCachedStream.Write(packet.bodyBytes, 0, packet.bodyBytes.Length);
            mCachedStream.WriteTo(destination);
            return true;
        }

        public NetPackageHeader DeserializePackageHeader(Stream source)
        {
            NetPackageHeader packetHeader = new NetPackageHeader();
            if (source is MemoryStream memoryStream)
            {
                byte[] bytes = memoryStream.GetBuffer();
                int packetSize = BitConverter.ToInt32(bytes, 0);
                packetHeader.PacketLength = packetSize;
                packetHeader.Id = BitConverter.ToInt16(bytes, 4);
                return packetHeader;
            }
            return null;
        }

        public NetPackage DeserializePackage(Stream source)
        {
            NetPackage packet = new NetPackage();
            if (source is MemoryStream memoryStream)
            {
                if (memoryStream != null)
                    packet.bodyBytes = memoryStream.ToArray();
            }
            return packet;
        }

        public object DeSerializeMessage(Type type, byte[] bytes)
        {
            if (bytes == null) return null;
            object o;
            using (var stream = new MemoryStream(bytes))
            {
                o = ProtoBuf.Serializer.Deserialize(type, stream);
            }
            return o;
        }
    }
}

