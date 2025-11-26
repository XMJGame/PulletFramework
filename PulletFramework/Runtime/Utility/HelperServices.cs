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
using System.IO;
using UnityEngine;
using YooAsset;

namespace PulletFramework.Utility
{
	public class BundleStream : FileStream
	{
		public const byte KEY = 64;

		public BundleStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access, share)
		{
		}
		public BundleStream(string path, FileMode mode) : base(path, mode)
		{
		}

		public override int Read(byte[] array, int offset, int count)
		{
			var index = base.Read(array, offset, count);
			for (int i = 0; i < array.Length; i++)
			{
				array[i] ^= KEY;
			}
			return index;
		}
	}

	/// <summary>
	/// 内置文件查询服务类
	/// </summary>
	public class GameQueryServices : IBuildinQueryServices
	{
        public bool Query(string packageName, string fileName, string fileCRC)
        {
			var package = YooAssets.GetPackage(packageName);
			if (package == null) return false;

			return StreamingAssetsHelper.FileExists($"{package.GetPackageBuildinRootDirectory()}/{packageName}/{fileName}");
		}
	}

	/// <summary>
	/// 远端资源地址查询服务类
	/// </summary>
	public class RemoteServices : IRemoteServices
	{
		private readonly string _defaultHostServer;
		private readonly string _fallbackHostServer;

		public RemoteServices(string defaultHostServer, string fallbackHostServer)
		{
			_defaultHostServer = defaultHostServer;
			_fallbackHostServer = fallbackHostServer;
		}
		string IRemoteServices.GetRemoteFallbackURL(string fileName)
		{
			return $"{_defaultHostServer}/{fileName}";
		}
		string IRemoteServices.GetRemoteMainURL(string fileName)
		{
			return $"{_fallbackHostServer}/{fileName}";
		}
	}

	/// <summary>
	/// 资源文件流加载解密类
	/// </summary>
	public class FileStreamDecryption : IDecryptionServices
	{
		/// <summary>
		/// 同步方式获取解密的资源包对象
		/// 注意：加载流对象在资源包对象释放的时候会自动释放
		/// </summary>
		AssetBundle IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo, out Stream managedStream)
		{
			BundleStream bundleStream = new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			managedStream = bundleStream;
			return AssetBundle.LoadFromStream(bundleStream, fileInfo.ConentCRC, GetManagedReadBufferSize());
		}

		/// <summary>
		/// 异步方式获取解密的资源包对象
		/// 注意：加载流对象在资源包对象释放的时候会自动释放
		/// </summary>
		AssetBundleCreateRequest IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo, out Stream managedStream)
		{
			BundleStream bundleStream = new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			managedStream = bundleStream;
			return AssetBundle.LoadFromStreamAsync(bundleStream, fileInfo.ConentCRC, GetManagedReadBufferSize());
		}

		private static uint GetManagedReadBufferSize()
		{
			return 1024;
		}
	}

	/// <summary>
	/// 资源文件偏移加载解密类
	/// </summary>
	public class FileOffsetDecryption : IDecryptionServices
	{
		/// <summary>
		/// 同步方式获取解密的资源包对象
		/// 注意：加载流对象在资源包对象释放的时候会自动释放
		/// </summary>
		AssetBundle IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo, out Stream managedStream)
		{
			managedStream = null;
			return AssetBundle.LoadFromFile(fileInfo.FileLoadPath, fileInfo.ConentCRC, GetFileOffset());
		}

		/// <summary>
		/// 异步方式获取解密的资源包对象
		/// 注意：加载流对象在资源包对象释放的时候会自动释放
		/// </summary>
		AssetBundleCreateRequest IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo, out Stream managedStream)
		{
			managedStream = null;
			return AssetBundle.LoadFromFileAsync(fileInfo.FileLoadPath, fileInfo.ConentCRC, GetFileOffset());
		}

		private static ulong GetFileOffset()
		{
			return 32;
		}
	}

	/// <summary>
	/// 默认的分发资源查询服务类
	/// </summary>
	//public class DefaultDeliveryQueryServices : IDeliveryQueryServices
	//{
	//	public DeliveryFileInfo GetDeliveryFileInfo(string packageName, string fileName)
	//	{
	//		throw new NotImplementedException();
	//	}
	//	public bool QueryDeliveryFiles(string packageName, string fileName)
	//	{
	//		return false;
	//	}
	//}
}