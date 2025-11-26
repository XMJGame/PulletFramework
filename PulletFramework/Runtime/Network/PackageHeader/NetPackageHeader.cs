
namespace PulletFramework.Network
{
    /// <summary>
    /// 网络消息包头接口。
    /// </summary>
    public class NetPackageHeader
    {
        /// <summary>
        /// 包的长度
        /// </summary>
        public int PacketLength
        {
            get;
            set;
        }

        /// <summary>
        /// 消息id
        /// </summary>
        public int Id
        {
            get;
            set;
        }
        public bool IsValid
        {
            get
            {
                return Id > 0 && PacketLength >= 0;
            }
        }

        public void Clear()
        {
            Id = 0;
            PacketLength = 0;
        }
    }
}
