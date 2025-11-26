using PulletFramework.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YooAsset;

namespace PulletFramework.Editor
{
    /// <summary>
    /// 文件加密
    /// </summary>
    public class FileOffsetEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            if (fileInfo.BundleName.Contains("assembly") || fileInfo.BundleName.Contains("configs"))
            {
                int offset = 32;
                byte[] fileData = File.ReadAllBytes(fileInfo.FilePath);
                var encryptedData = new byte[fileData.Length + offset];
                Buffer.BlockCopy(fileData, 0, encryptedData, offset, fileData.Length);

                EncryptResult result = new EncryptResult();
                result.Encrypted = true;
                result.EncryptedData = encryptedData;
                return result;
            }
            else
            {
                EncryptResult result = new EncryptResult();
                result.Encrypted = false;
                return result;
            }
        }
    }

    public class FileStreamEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            // LoadFromStream
            if (fileInfo.BundleName.Contains("assembly") || fileInfo.BundleName.Contains("configs"))
            {
                var fileData = File.ReadAllBytes(fileInfo.FilePath);
                for (int i = 0; i < fileData.Length; i++)
                {
                    fileData[i] ^= BundleStream.KEY;
                }

                EncryptResult result = new EncryptResult();
                result.Encrypted = true;
                result.EncryptedData = fileData;
                return result;
            }

            // Normal
            {
                EncryptResult result = new EncryptResult();
                result.Encrypted = false;
                return result;
            }
        }
    }
}