
using System;
using System.IO;

namespace PulletFramework.Network
{
    public delegate void HandleErrorDelegate(bool isDispose, string error);
    public interface IChannelHelper
    {
        /// <summary>
        /// 获取消息包头长度。
        /// </summary>
        int PackageHeaderLength
        {
            get;
        }

        /// <summary>
        /// 自动心跳间隔，单位秒；小于等于零表示关闭自动心跳。
        /// </summary>
        int HeartBeatInterval
        {
            get;
        }
        /// <summary>
        /// 获取心跳消息包。
        /// </summary>
        /// <returns>已编码前的心跳消息包；返回null表示本次不发送。</returns>
        NetPackage GetHeartBeat();

        /// <summary>
        /// 序列化消息包。
        /// </summary>
        /// <typeparam name="T">消息包类型。</typeparam>
        /// <param name="packet">要序列化的消息包。</param>
        /// <param name="destination">要序列化的目标流。</param>
        /// <returns>是否序列化成功。</returns>
        bool Serialize<T>(T packet, Stream destination) where T : NetPackage;

        /// <summary>
        /// 反序列消息包头。
        /// </summary>
        /// <param name="source">要反序列化的来源流。</param>
        /// <returns>反序列化后的消息包头。</returns>
        NetPackageHeader DeserializePackageHeader(Stream source);

        /// <summary>
        /// 反序列化消息包。
        /// </summary>
        /// <param name="source">要反序列化的来源流。</param>
        /// <returns>反序列化后的消息包。</returns>
        NetPackage DeserializePackage(Stream source);


        /// <summary>
        /// 反序列化消息
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        object DeSerializeMessage(Type type, byte[] bytes);

        /// <summary>
        /// 注册异常错误回调方法
        /// </summary>
        /// <param name="callback"></param>
        void RigistHandleErrorCallback(HandleErrorDelegate callback);
    }
}
