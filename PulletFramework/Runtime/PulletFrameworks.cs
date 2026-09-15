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
using PulletFramework.Setting;
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
		private static bool mIsDestroying = false;
		private static GameObject mGameObject;
		private static GameObject gameObject { get { return mGameObject; } }
		private static Transform transform { get { return mGameObject.transform; } }

		private static MonoBehaviour mMono;
		public static MonoBehaviour mono { get { return mMono; } }
		public static bool IsInitialized => mIsInitialize && mGameObject != null && mMono != null;

		/// <summary>
		/// 初始化框架
		/// </summary>
		public static void Initialize()
		{
			if (mIsDestroying)
				throw new InvalidOperationException($"{nameof(PulletFrameworks)} is being destroyed.");
			if (mIsInitialize && (mGameObject == null || mMono == null))
				ResetStaticState();
			if (mIsInitialize)
				throw new Exception($"{nameof(PulletFramework)} is initialized !");

			if (mIsInitialize == false)
			{
				PLogger.Level = PulletSettingsData.Setting.logLevel;
				// 创建驱动器
				mIsInitialize = true;
				mGameObject = new UnityEngine.GameObject($"[{nameof(PulletFramework)}]");
				mMono = mGameObject.AddComponent<PulletFrameworkLifecycleDriver>();
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
			PulletOperationSystem.Update();
			PulletSound.Update(deltaTime, unscaledDeltaTime);
			PulletEvent.Update(deltaTime, unscaledDeltaTime);
			PulletWindow.Update(deltaTime, unscaledDeltaTime);
			UISafeArea.Update();
			PulletPooling.Update(deltaTime, unscaledDeltaTime);
			PulletNetwork.Update(deltaTime, unscaledDeltaTime);
		}


		/// <summary>
		/// 销毁框架
		/// </summary>
		public static void Destroy()
		{
			DestroyInternal(true);
		}

		internal static void OnDriverDestroyed(PulletFrameworkLifecycleDriver driver)
		{
			if (!ReferenceEquals(mMono, driver))
				return;
			DestroyInternal(false);
		}

		private static void DestroyInternal(bool destroyRoot)
		{
			if (mIsDestroying || (!mIsInitialize && mGameObject == null && mMono == null))
				return;

			mIsDestroying = true;
			GameObject root = mGameObject;
			mIsInitialize = false;
			mGameObject = null;
			mMono = null;
			try
			{
				SafeDestroy(nameof(PulletForm), PulletForm.Destroy);
				SafeDestroy(nameof(PulletSound), PulletSound.Destroy);
				SafeDestroy(nameof(PulletPlayerPrefs), () => PulletPlayerPrefs.UninstallBackend());
				SafeDestroy(nameof(PulletEvent), PulletEvent.Destroy);
				SafeDestroy(nameof(PulletWindow), PulletWindow.Destroy);
				SafeDestroy(nameof(UISafeArea), UISafeArea.ResetProvider);
				SafeDestroy(nameof(PulletUIFeedback), PulletUIFeedback.Reset);
				SafeDestroy(nameof(PulletPooling), PulletPooling.Destroy);
				SafeDestroy(nameof(PulletNetwork), PulletNetwork.Destroy);
				SafeDestroy(nameof(PulletOperationSystem), PulletOperationSystem.Clear);
				SafeDestroy(nameof(Resource.PulletResources), Resource.PulletResources.Uninstall);
				if (destroyRoot && root != null)
					GameObject.Destroy(root);
				PLogger.Log($"{nameof(PulletFramework)} destroy all !");
			}
			finally
			{
				mIsDestroying = false;
			}
		}

		private static void SafeDestroy(string subsystem, Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				PLogger.Exception(exception, $"Destroy subsystem failed: {subsystem}");
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetRuntimeState()
		{
			DestroyInternal(true);
			ResetStaticState();
		}

		private static void ResetStaticState()
		{
			mIsInitialize = false;
			mIsDestroying = false;
			mGameObject = null;
			mMono = null;
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
