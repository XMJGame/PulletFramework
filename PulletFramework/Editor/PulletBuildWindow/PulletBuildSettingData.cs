using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace PulletFramework.Editor
{
	public class PulletBuildSettingData
	{
		private static PulletBuildSetting mSetting = null;
		public static PulletBuildSetting Setting
		{
			get
			{
				if (mSetting == null)
					mSetting = SettingLoader.LoadSettingData<PulletBuildSetting>("Pullets");
				return mSetting;
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
				EditorUtility.SetDirty(Setting);
				AssetDatabase.SaveAssets();
				Debug.Log($"{nameof(PulletBuildSetting)}.asset is saved!");
			}
		}
	}
}