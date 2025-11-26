#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Xu Mingjun(Xinxiang, Henan) All Rights Reserved.
//  作    者：许明俊
//  创建日期：2020
//  功能描述：PulletFramework 框架（别名：小母鸡框架，名字首字母而起）
//
// *********************************************************************
#endregion
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace PulletFramework
{
    internal static class PLogger
    {
        [Conditional("DEBUG")]
        public static void Log(string info)
        {
            UnityEngine.Debug.Log("[PulletFramework] " + info);
        }

        public static void DebugLog(string info)
        {
            UnityEngine.Debug.Log("[PulletFramework] " + info);
        }
        public static void Warning(string info)
        {
            UnityEngine.Debug.LogWarning("[PulletFramework] " + info);
        }
        public static void Error(string info)
        {
            UnityEngine.Debug.LogError("[PulletFramework] " + info);
        }
    }
}
