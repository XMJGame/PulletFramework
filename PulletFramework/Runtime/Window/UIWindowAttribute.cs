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

		/// <summary>
		/// 当前窗口显示时是否隐藏 NavigationLayer 窗口。
		/// </summary>
		public bool hidePersistent = false;

		/// <summary>
		/// 窗口关闭后的资源保留策略。
		/// </summary>
		public EWindowCachePolicy cachePolicy = EWindowCachePolicy.Cache;

		public UIWindowAttribute() { }
		public UIWindowAttribute(
			EWindowLayer windowLayer,
			bool fullScreen = true,
			bool hidePersistent = false,
			EWindowCachePolicy cachePolicy = EWindowCachePolicy.Cache)
		{
			this.windowLayer = windowLayer;
			this.fullScreen = fullScreen;
			this.hidePersistent = hidePersistent;
			this.cachePolicy = cachePolicy;
		}
	}
}
