#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架 - 辅助开发
//
// *********************************************************************
#endregion
using UnityEngine;
using UnityEngine.Networking;

namespace PulletFramework.Utility
{
    public sealed class StreamingAssetsHelper
    {
        public static bool FileExists(string filePath)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, filePath);
#if UNITY_ANDROID && !UNITY_EDITOR
        UnityWebRequest request = null;
        try
        {
            request = UnityWebRequest.Get(path);
            request.SendWebRequest();
            while (!request.isDone) { }
            if (string.IsNullOrEmpty(request.error)) 
            {
                      Debug.Log("FileExists："+ request.error);
                request.Dispose();
                return true;
            }
            request.Dispose();
            return false;
        }
        catch (System.Exception exception)
        {
            request.Dispose();
            Debug.Log("No FileExists："+ exception.Message);
            return false;
        }
		//return System.IO.File.Exists(System.IO.Path.Combine(Application.streamingAssetsPath, filePath));
#else
            return System.IO.File.Exists(path);
#endif
        }

    }
}