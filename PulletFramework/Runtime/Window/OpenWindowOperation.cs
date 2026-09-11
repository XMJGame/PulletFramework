#region Copyright (C)
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架
//
// *********************************************************************
#endregion
namespace PulletFramework.Window
{
    public class OpenWindowOperation : PulletAsyncOperation
    {
        private enum ESteps
        {
            None,
            Waiting,
            Done,
        }

        private readonly UIWindow _window;
        private readonly string _initialError;
        private readonly bool _ownsOpenAttempt;
        private ESteps _steps = ESteps.None;

        /// <summary>
        /// 本次打开的窗口实例。
        /// </summary>
        public UIWindow Window => _window;

        internal OpenWindowOperation(
            UIWindow window,
            string initialError = null,
            bool ownsOpenAttempt = false)
        {
            _window = window;
            _initialError = initialError;
            _ownsOpenAttempt = ownsOpenAttempt;
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
                if (!string.IsNullOrEmpty(_initialError))
                {
                    _steps = ESteps.Done;
                    SetFailed(_initialError);
                    return;
                }

                if (_window == null)
                {
                    _steps = ESteps.Done;
                    SetFailed("Window is invalid.");
                    return;
                }

                if (!string.IsNullOrEmpty(_window.LoadError))
                {
                    _steps = ESteps.Done;
                    SetFailed(_window.LoadError);
                    return;
                }

                if (!_window.IsOpenCompleted)
                    return;

                _steps = ESteps.Done;
                SetSucceeded();
            }
        }

        protected override void OnWaitForAsyncComplete()
        {
            if (_window != null)
            {
                if (_steps == ESteps.Done)
                    return;
                _window.WaitForLoadComplete();
                OnUpdate();
            }
        }

        protected override void OnAbort()
        {
            _steps = ESteps.Done;
            if (_window != null && _ownsOpenAttempt)
                PulletWindow.CancelOpen(_window);
        }
    }
}
