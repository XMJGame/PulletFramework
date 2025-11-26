using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;
using COSXML.Model.Bucket;
using COSXML.CosException;
using System;
using System.Threading.Tasks;
using COSXML.Transfer;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PulletFramework.Utility;
using UnityEditor;

namespace PulletFramework.Editor
{
    /// <summary>
    /// 腾讯COS
    /// </summary>
    public class TencentCOS
    {
        internal static CosXmlServer cosXml;
        private static bool mInit = false;
        public static void Init()
        {
            if (mInit) return;
            mInit = true;
            //// 腾讯云 SecretId
            //string secretId = "";
            //// 腾讯云 SecretKey
            //string secretKey = "";
            // 存储桶所在地域
            string region = "ap-guangzhou";

            // 普通初始化方式
            CosXmlConfig config = new CosXmlConfig.Builder()
                .SetRegion(region)
                .SetDebugLog(true)
                .Build();


            long keyDurationSecond = 600;
            QCloudCredentialProvider qCloudCredentialProvider = new DefaultQCloudCredentialProvider(PulletEditorSettingData.Setting.secretId, PulletEditorSettingData.Setting.secretKey, keyDurationSecond);

            // service 初始化完成
            cosXml = new CosXmlServer(config, qCloudCredentialProvider);
        }

        public static async Task<String> PutObject(string key, string srcPath)
        {
            if (!mInit)
                Init();

      
            string cosKey = $"{PulletEditorSettingData.Setting.cosKey}/{key}";// PulletEditorSettingData.Setting.cosKey+ key;
            // 初始化 TransferConfig
            TransferConfig transferConfig = new TransferConfig();

            // 初始化 TransferManager
            TransferManager transferManager = new TransferManager(cosXml, transferConfig);

            //对象在存储桶中的位置标识符，即称对象键
            String cosPath = cosKey;
            //本地文件绝对路径
            //String srcPath = srcPath;// @"D:\XuMingJun\New_HeHan\Clinet\AssetBundles\MainArt\1.0.2.zip";
            Debug.Log("开始上传:" + cosKey);
            // 上传对象
            COSXMLUploadTask uploadTask = new COSXMLUploadTask(PulletEditorSettingData.Setting.bucket, cosPath);
            uploadTask.SetSrcPath(srcPath);

            uploadTask.progressCallback = delegate (long completed, long total)
            {
                Debug.Log(String.Format("progress = {0:##.##}%", completed * 100.0 / total));
            };

            try
            {
                COSXML.Transfer.COSXMLUploadTask.UploadTaskResult result = await
                    transferManager.UploadAsync(uploadTask);
                Debug.Log(result.GetResultInfo());
                string eTag = result.eTag;
                EditorUtility.ClearProgressBar();
            }
            catch (Exception e)
            {
                Debug.LogError("CosException: " + e);
            }
            return cosKey;
        }
    }
}
