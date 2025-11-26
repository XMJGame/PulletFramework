#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架
//
// *********************************************************************
#endregion
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Singleton
{
	public abstract class SingletonInstance<T> where T : class, ISingleton
	{
		private static T _Instance;
		public static T Instance
		{
			get
			{
				if (_Instance == null)
				{
					PLogger.Warning($"{typeof(T)} is not create. Use {nameof(PulletSingleton)}.{nameof(PulletSingleton.CreateSingleton)} create.");
					PLogger.Warning("This is created automatically for fault tolerance");
					PulletSingleton.CreateSingleton<T>();
				}
				return _Instance;
			}
		}

		protected SingletonInstance()
		{
			if (_Instance != null)
				throw new System.Exception($"{typeof(T)} instance already created.");
			_Instance = this as T;
		}
		protected void DestroyInstance()
		{
			_Instance = null;
		}
	}
}
