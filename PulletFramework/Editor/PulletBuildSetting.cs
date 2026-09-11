using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace PulletFramework.Editor
{
	/// <summary>
	/// 构建平台
	/// </summary>
	public enum EBuildTarget
	{
		Android,
		iOS,
		StandaloneWindows,
		StandaloneWindows64,
		StandaloneOSX,
		WebGL,
	}

	/// <summary>
	/// 拷贝到指定目录操作
	/// </summary>
	public enum ECopyAssetBundleOperation
	{
		None,
		Copy,
		CopyAndPack_Zip,
		CopyAndUpload
	}

	public enum EPulletResourceBuildMode
	{
		Incremental,
		ForceRebuild
	}

	// 数值与 YooAsset 对应枚举保持一致，确保旧配置资产可以无损迁移。
	public enum EPulletBuildPipeline
	{
		EditorSimulateBuildPipeline = 0,
		LegacyBuildPipeline = 1,
		ScriptableBuildPipeline = 2,
		RawFileBuildPipeline = 3,
		InstantAssetBuildPipeline = 4,
		ArchiveFileBuildPipeline = 5
	}

	public enum EPulletCompressOption
	{
		Uncompressed = 0,
		LZMA = 1,
		LZ4 = 2
	}

	public enum EPulletFileNameStyle
	{
		HashName = 0,
		BundleName = 1,
		BundleName_HashName = 2
	}

	public enum EPulletBundledCopyOption
	{
		None = 0,
		ClearAndCopyAll = 1,
		ClearAndCopyByTags = 2,
		OnlyCopyAll = 3,
		OnlyCopyByTags = 4
	}

	[CreateAssetMenu(fileName = "PulletBuildSetting", menuName = "Pullet/Create Pullet Build Settings")]
	public class PulletBuildSetting : ScriptableObject
	{
		/// <summary>
		/// 构建平台
		/// </summary>
		public EBuildTarget buildTarget;

		/// <summary>
		/// 版本
		/// </summary>
		public string appVersion = "1.0.0";

		/// <summary>
		/// 版本号
		/// </summary>
		public int appVersionCode = 0;

		/// <summary>
		/// apk名称
		/// </summary>
		public string apkName;

		/// <summary>
		/// HybridCLR Settings
		/// </summary>
		public bool showHybridCLRSettings = false;
		/// <summary>
		/// AOT
		/// </summary>
		public bool generateAOTDlls;
		public bool copyAOTDlls;

		/// <summary>
		/// 热更
		/// </summary>
		public bool compileHotUpdateDlls;
		public bool copyHotUpdateDlls;

		/// <summary>
		/// AssetBundle Info Settings
		/// </summary>
		public bool showAssetBundleInfoSettings = false;

		/// <summary>
		/// 构建管线
		/// </summary>
		public EPulletBuildPipeline buildPipeline = EPulletBuildPipeline.LegacyBuildPipeline;

		/// <summary>
		/// 构建模式
		/// </summary>
		public EPulletResourceBuildMode buildMode = EPulletResourceBuildMode.ForceRebuild;

		/// <summary>
		/// 构建包名
		/// </summary>
		public string buildPackage;

		/// <summary>
		/// 加密类名称
		/// </summary>
		public string encyptionClassName;

		/// <summary>
		/// 压缩方式
		/// </summary>
		public EPulletCompressOption compressOption = EPulletCompressOption.LZ4;

		/// <summary>
		/// 输出资源包名称样式
		/// </summary>
		public EPulletFileNameStyle outputNameStyle = EPulletFileNameStyle.HashName;

		/// <summary>
		/// 首包资源文件的拷贝方式
		/// </summary>
		[FormerlySerializedAs("copyBuildinFileOption")]
		public EPulletBundledCopyOption bundledCopyOption = EPulletBundledCopyOption.ClearAndCopyAll;

		/// <summary>
		/// 变体收集
		/// </summary>
		public bool shaderVariantCollector;

		/// <summary>
		/// 拷贝ab 操作
		/// </summary>
		public ECopyAssetBundleOperation copyAssetBundleOperation;

		public bool copyAssetBundle;

		//打包是否构建 AssetBundles
		public bool buildAssetBundles;

		public BuildTarget GetBuildTarget()
		{
			switch (buildTarget)
			{
				case EBuildTarget.Android:
					return BuildTarget.Android;
				case EBuildTarget.iOS:
					return BuildTarget.iOS;
				case EBuildTarget.StandaloneWindows:
					return BuildTarget.StandaloneWindows;
				case EBuildTarget.StandaloneWindows64:
					return BuildTarget.StandaloneWindows64;
				case EBuildTarget.StandaloneOSX:
					return BuildTarget.StandaloneOSX;
				case EBuildTarget.WebGL:
					return BuildTarget.WebGL;
				default:
					return BuildTarget.Android;
			}
		}

		public string GetOutputPath()
		{
			return $"{PulletEditorSettingData.Setting.assetBundleCopyPath}/{buildPackage}/{appVersion}/{buildTarget}";
		}

		public string GetPackagPath()
		{
			return $"{PulletEditorSettingData.Setting.assetBundleCopyPath}/{buildPackage}";
		}

		public string GetVersionPath()
		{
			return $"{PulletEditorSettingData.Setting.assetBundleCopyPath}/{buildPackage}/{appVersion}";
		}
	}
}
