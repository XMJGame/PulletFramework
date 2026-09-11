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
			for (int i = offset; i < offset + index; i++)
			{
				array[i] ^= KEY;
			}
			return index;
		}
	}
}
