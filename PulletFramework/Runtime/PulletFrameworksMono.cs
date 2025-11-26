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
using UnityEngine;

namespace PulletFramework
{
    public class PulletFrameworksMono : MonoBehaviour
    {
        internal void Update()
        {
            PulletFrameworks.Update(Time.deltaTime, Time.unscaledDeltaTime);
        }
    }
}
