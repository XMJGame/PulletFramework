#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架 - 辅助开发
//
// *********************************************************************
#endregion
using System;
namespace PulletFramework.Window
{
    [AttributeUsage(AttributeTargets.Class)]
	public class UIWindowAttribute : Attribute
	{
		/// <summary>
		/// 窗口层级
		/// </summary>
		public EWindowLayer windowLayer = EWindowLayer.NormalLayer;

		/// <summary>
		/// 全屏窗口标记
		/// </summary>
		public bool fullScreen = true;

		public UIWindowAttribute() { }
		public UIWindowAttribute(EWindowLayer windowLayer, bool fullScreen = true)
		{
			this.windowLayer = windowLayer;
			this.fullScreen = fullScreen;
		}
	}
}
