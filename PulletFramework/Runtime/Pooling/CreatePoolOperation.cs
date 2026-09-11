#region Copyright (C)
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架 - 网络框架
//
// *********************************************************************
#endregion
using UnityEngine;
using PulletFramework.Resource;

namespace PulletFramework.Pooling
{
    public class CreatePoolOperation : PulletAsyncOperation
    {
        private enum ESteps
        {
            None,
            Waiting,
            Done,
        }

        private readonly IResourceAssetHandle _handle;
        private ESteps _steps = ESteps.None;
        public GameObject AssetObject
        {
            get { return _handle != null ? (GameObject)_handle.AssetObject : null; }
        }
        internal CreatePoolOperation(IResourceAssetHandle handle)
        {
            _handle = handle;
        }
        protected override void OnStart()
        {
            _steps = ESteps.Waiting;
        }
        protected override void OnUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.Waiting)
            {
                if (_handle.IsValid == false)
                {
                    _steps = ESteps.Done;
                    SetFailed("Resource asset handle is invalid.");
                    return;
                }

                if (_handle.IsDone == false)
                    return;

                if (_handle.AssetObject == null)
                {
                    _steps = ESteps.Done;
                    SetFailed("Loaded asset is null.");
                    return;
                }

                _steps = ESteps.Done;
                SetSucceeded();
            }
        }

        protected override void OnWaitForAsyncComplete()
        {
            if (_handle != null)
            {
                if (_steps == ESteps.Done)
                    return;
                _handle.WaitForCompletion();
                OnUpdate();
            }
        }

        protected override void OnAbort()
        {
        }
    }
}
