#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架
//
// *********************************************************************
#endregion
using UnityEngine;

namespace PulletFramework.Singleton
{
	internal class PulletSingletonMono : MonoBehaviour
    {
        internal void Update()
        {
            PulletSingleton.Update();
        }

        internal void OnDestroy()
        {
            PulletSingleton.Destroy();
        }
    }
}
