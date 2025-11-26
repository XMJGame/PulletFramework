
using System;

namespace PulletFramework.Network
{
    /// <summary>
    /// 网络消息处理
    /// </summary>
    public abstract class NetMessageHandler
    {
        public abstract int Id
        {
            get;
        }

        public abstract void Handle(object message);

        public abstract Type GetMessageType();
    }
}
