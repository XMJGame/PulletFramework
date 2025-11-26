using System;
namespace PulletFramework.Network
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PacketHandlerAttribute : Attribute
    {
        public PacketHandlerAttribute(int msgId)
        {
           //MessageDistribute.AddNetPackageHandler(msgId);
        }
    }
}
