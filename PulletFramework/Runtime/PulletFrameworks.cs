#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Xu Mingjun(Xinxiang, Henan) All Rights Reserved.
//  作    者：许明俊
//  创建日期：2020
//  功能描述：PulletFramework 框架（别名：小母鸡框架，名字首字母而起）
//
// *********************************************************************
#endregion
using PulletFramework.Event;
using PulletFramework.Form;
using PulletFramework.Network;
using PulletFramework.Pooling;
using PulletFramework.Sound;
using PulletFramework.Window;
using System;
using System.Collections;
using UnityEngine;

namespace PulletFramework
{
	/// <summary>
	/// 框架相关
	/// </summary>
    public static class PulletFrameworks
	{
		private static bool mIsInitialize = false;
		private static GameObject mGameObject;
		private static GameObject gameObject { get { return mGameObject; } }
		private static Transform transform { get { return mGameObject.transform; } }

		private static MonoBehaviour mMono;
		public static MonoBehaviour mono { get { return mMono; } }

		/// <summary>
		/// 初始化框架
		/// </summary>
		public static void Initialize()
		{
			if (mIsInitialize)
				throw new Exception($"{nameof(PulletFramework)} is initialized !");

			if (mIsInitialize == false)
			{
				// 创建驱动器
				mIsInitialize = true;
				mGameObject = new UnityEngine.GameObject($"[{nameof(PulletFramework)}]");
				mMono = mGameObject.AddComponent<PulletFrameworksMono>();
				UnityEngine.Object.DontDestroyOnLoad(mGameObject);
				PLogger.Log($"{nameof(PulletFramework)} initalize !");
			}
		}

		/// <summary>
		/// 添加子系统
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		internal static GameObject AddSubsystemGameObject(string name) 
		{
			if (!mIsInitialize) 
			{
				Initialize();
			}
			GameObject obj = new UnityEngine.GameObject(name);
			obj.transform.SetParent(transform);
			return obj;
		}

		/// <summary>
		/// 框架更新
		/// </summary>
		internal static void Update(float deltaTime, float unscaledDeltaTime) 
		{
			PulletSound.Update(deltaTime, unscaledDeltaTime);
			PulletEvent.Update(deltaTime, unscaledDeltaTime);
			PulletWindow.Update(deltaTime, unscaledDeltaTime);
			PulletPooling.Update(deltaTime, unscaledDeltaTime);
			PulletNetwork.Update(deltaTime, unscaledDeltaTime);
		}


		/// <summary>
		/// 销毁框架
		/// </summary>
		public static void Destroy()
		{
			if (mIsInitialize)
			{
				PulletForm.Destroy();
				PulletSound.Destroy();
				PulletEvent.Destroy();
				PulletWindow.Destroy();
				PulletPooling.Destroy();
				PulletNetwork.Destroy();
				mIsInitialize = false;
				if (mGameObject != null)
					GameObject.Destroy(mGameObject);
				PLogger.Log($"{nameof(PulletFramework)} destroy all !");
			}
		}

		/// <summary>
		/// 开启一个协程
		/// </summary>
		public static Coroutine StartCoroutine(IEnumerator coroutine)
		{
			if (!mIsInitialize)
			{
				Initialize();
			}
			Debug.Log($"StartCoroutine: {coroutine.GetType().Name}  +   {mMono.name}");
            return mMono.StartCoroutine(coroutine);
		}
		public static Coroutine StartCoroutine(string methodName)
		{
			if (!mIsInitialize)
			{
				Initialize();
			}
			return mMono.StartCoroutine(methodName);
		}

		/// <summary>
		/// 停止一个协程
		/// </summary>
		public static void StopCoroutine(Coroutine coroutine)
		{
			mMono.StopCoroutine(coroutine);
		}
		public static void StopCoroutine(string methodName)
		{
			mMono.StopCoroutine(methodName);
		}

		/// <summary>
		/// 停止所有协程
		/// </summary>
		public static void StopAllCoroutines()
		{
			mMono.StopAllCoroutines();
		}
	}
}
