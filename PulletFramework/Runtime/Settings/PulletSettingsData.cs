#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Xu Mingjun(Xinxiang, Henan) All Rights Reserved.
//  作    者：许明俊
//  创建日期：2020
//  功能描述：PulletFramework 框架（别名：小母鸡框架，名字首字母而起）
//
// *********************************************************************
#endregion
using UnityEngine;

namespace PulletFramework.Setting
{
	/// <summary>
	/// 
	/// </summary>
    public class PulletSettingsData
    {
		private static PulletSettings mSetting = null;
		public static PulletSettings Setting
		{
			get
			{
				if (mSetting == null)
					LoadSettingData();
				return mSetting;
			}
		}


		/// <summary>
		/// 加载配置文件
		/// </summary>
		private static void LoadSettingData()
		{
			mSetting = Resources.Load<PulletSettings>("PulletSettings");
			if (mSetting == null)
			{
				PLogger.Log("Pullet use default settings.");
				mSetting = ScriptableObject.CreateInstance<PulletSettings>();
#if UNITY_EDITOR
				string filePath = $"Assets/Settings/Pullets/Resources/PulletSettings.asset";
				if (!System.IO.File.Exists(filePath))
				{
					System.IO.Directory.CreateDirectory(filePath);
				}
				UnityEditor.AssetDatabase.CreateAsset(mSetting, filePath);
				UnityEditor.AssetDatabase.SaveAssets();
				UnityEditor.AssetDatabase.Refresh();
#endif
			}
			else
			{
				PLogger.Log("Pullet use user settings.");
			}
		}

		/// <summary>
		/// 配置数据是否被修改
		/// </summary>
		public static bool IsDirty { set; get; } = false;

		/// <summary>
		/// 存储配置文件
		/// </summary>
		public static void SaveFile()
		{
			if (Setting != null)
			{
				IsDirty = false;
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(Setting);
				UnityEditor.AssetDatabase.SaveAssets();
				Debug.Log($"{nameof(PulletSettings)}.asset is saved!");
#endif
			}
		}
	}
}
