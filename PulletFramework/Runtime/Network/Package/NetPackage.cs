
namespace PulletFramework.Network
{
    public class NetPackage
    {
		/// <summary>
		/// 消息ID
		/// </summary>
		public int msgId { set; get; }

		/// <summary>
		/// 包体数据
		/// </summary>
		public byte[] bodyBytes = null;
	}
}
