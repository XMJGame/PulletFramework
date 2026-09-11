using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.Editor
{
	public class SettingLoader
	{
		/// <summary>
		/// 加载相关的配置文件
		/// </summary>
		public static TSetting LoadSettingData<TSetting>(string pathName = "") where TSetting : ScriptableObject
		{
			var settingType = typeof(TSetting);
			var guids = AssetDatabase.FindAssets($"t:{settingType.Name}");
			if (guids.Length == 0)
			{
				PLogger.EditorWarning($"Create new {settingType.Name}.asset");
				var setting = ScriptableObject.CreateInstance<TSetting>();
				string directory = string.IsNullOrWhiteSpace(pathName)
					? "Assets/Settings"
					: $"Assets/Settings/{pathName.Trim('/')}";
				EnsureAssetFolder(directory);
				string filePath = $"{directory}/{settingType.Name}.asset";
				AssetDatabase.CreateAsset(setting, filePath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				return setting;
			}
			else
			{
				if (guids.Length != 1)
				{
					foreach (var guid in guids)
					{
						string path = AssetDatabase.GUIDToAssetPath(guid);
						PLogger.EditorWarning($"Found multiple file : {path}");
					}
					throw new System.Exception($"Found multiple {settingType.Name} files !");
				}

				string filePath = AssetDatabase.GUIDToAssetPath(guids[0]);
				var setting = AssetDatabase.LoadAssetAtPath<TSetting>(filePath);
				return setting;
			}
		}

		private static void EnsureAssetFolder(string folderPath)
		{
			string normalizedPath = folderPath.Replace('\\', '/').TrimEnd('/');
			if (!normalizedPath.StartsWith("Assets", StringComparison.Ordinal))
				throw new ArgumentException("Settings must be stored below Assets.", nameof(folderPath));

			string[] parts = normalizedPath.Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = $"{current}/{parts[i]}";
				if (!AssetDatabase.IsValidFolder(next))
					AssetDatabase.CreateFolder(current, parts[i]);
				current = next;
			}
		}
	}
}
