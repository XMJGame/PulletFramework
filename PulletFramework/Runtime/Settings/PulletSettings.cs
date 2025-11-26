#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Xu Mingjun(Xinxiang, Henan) All Rights Reserved.
//  作    者：许明俊
//  创建日期：2020
//  功能描述：PulletFramework 框架（别名：小母鸡框架，名字首字母而起）
//
// *********************************************************************
#endregion
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace PulletFramework.Setting
{
    [CreateAssetMenu(fileName = "PulletSettings", menuName = "Pullet/Create Pullet Settings")]
    public class PulletSettings : ScriptableObject
    {
        /// <summary>
        /// 默认的构建管线
        /// </summary>
        public EDefaultBuildPipeline defaultBuildPipeline = EDefaultBuildPipeline.BuiltinBuildPipeline;

        /// <summary>
        /// 运行时资产模式
        /// </summary>
        public EPlayMode runPlayMode = EPlayMode.HostPlayMode;

        /// <summary>
        /// 默认服务器
        /// </summary>
        public string defaultHostServer;

        /// <summary>
        /// 备用服务器
        /// </summary>
        public string fallbackHostServer;

        //默认的资源包
        public string defaultPackageName = "DefaultPackage";

        //默认程序及名称
        public string defaultHotUpdateAssemblyName;

        /// <summary>
        /// 热更程序集
        /// </summary>
        public List<string> hotUpdateAssemblies = new List<string>();

        /// <summary>
        /// AOT程序集
        /// </summary>
        public List<string> aotMetaAssemblys = new List<string>();

        public bool isEif;
        public string eifPath;
    }
}