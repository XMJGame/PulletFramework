using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace PulletFramework.Editor
{

	[CreateAssetMenu(fileName = "PulletEditorSetting", menuName = "Pullet/Create Pullet Editor Setting")]
	public class PulletEditorSetting : ScriptableObject
	{
		//android 签名文件
		public bool androidSetting;
		public string keystoreName = "";
		public string keystorePass = "";
		public string keyaliasName = "";
		public string keyaliasPass = "";

		//ab 包临时拷贝路径
		public string assetBundleCopyPath = "AssetBundles";

		public bool tencentCOSSetting;
		//腾讯云 SecretId
		public string secretId;
		//腾讯云 SecretKey
		public string secretKey;
		//那个桶
		public string bucket;
		//桶对应文件夹目录
		public string cosKey;
	}
}