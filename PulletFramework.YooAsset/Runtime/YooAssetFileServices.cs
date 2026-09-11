using System.IO;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    public sealed class YooAssetStreamDecryptor : IBundleStreamDecryptor
    {
        public int GetBufferSize(BundleDecryptArgs args) => 1024;

        public Stream CreateDecryptionStream(BundleDecryptArgs args)
        {
            return new YooAssetXorStream(
                args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    public sealed class YooAssetOffsetDecryptor : IBundleOffsetDecryptor
    {
        public long GetFileOffset(BundleDecryptArgs args) => 32;
    }
}
