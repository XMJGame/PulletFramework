using System.IO;
using PulletFramework.Utility;
using YooAsset;

namespace PulletFramework.YooAssetAdapter
{
    public sealed class YooAssetBuiltinFileAccessor : IBuiltinFileAccessor
    {
        public bool FileExists(string filePath) => StreamingAssetsHelper.FileExists(filePath);
        public byte[] ReadAllBytes(string filePath) => File.ReadAllBytes(filePath);
    }

    public sealed class YooAssetStreamDecryptor : IBundleStreamDecryptor
    {
        public int GetBufferSize(BundleDecryptArgs args) => 1024;

        public Stream CreateDecryptionStream(BundleDecryptArgs args)
        {
            return new BundleStream(args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    public sealed class YooAssetOffsetDecryptor : IBundleOffsetDecryptor
    {
        public long GetFileOffset(BundleDecryptArgs args) => 32;
    }
}
