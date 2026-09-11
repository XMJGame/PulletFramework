using System.IO;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>读取时对数据执行固定密钥异或解密的 AssetBundle 文件流。</summary>
    public sealed class YooAssetXorStream : FileStream
    {
        public const byte Key = 64;

        public YooAssetXorStream(
            string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
        }

        public override int Read(byte[] array, int offset, int count)
        {
            int bytesRead = base.Read(array, offset, count);
            for (int index = offset; index < offset + bytesRead; index++)
                array[index] ^= Key;
            return bytesRead;
        }
    }
}
