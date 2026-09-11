using System;
using System.IO;
using PulletFramework.YooAssetAdapter;
using YooAsset;

namespace PulletFramework.Editor
{
    /// <summary>
    /// 文件加密
    /// </summary>
    public class FileOffsetEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            const int offset = 32;
            byte[] fileData = File.ReadAllBytes(args.FilePath);
            var encryptedData = new byte[fileData.Length + offset];
            Buffer.BlockCopy(fileData, 0, encryptedData, offset, fileData.Length);
            return new BundleEncryptResult(true, encryptedData);
        }
    }

    public class FileStreamEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            byte[] fileData = File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < fileData.Length; i++)
            {
                fileData[i] ^= YooAssetXorStream.Key;
            }
            return new BundleEncryptResult(true, fileData);
        }
    }
}
