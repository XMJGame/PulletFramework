#region Copyright (C)
// ********************************************************************
//  Copyright (C) 2020-2024 Xu Mingjun(Xinxiang, Henan) All Rights Reserved.
//  作    者：许明俊
//  创建日期：2020
//  功能描述：PulletFramework 框架（别名：小母鸡框架，名字首字母而起）
//
// *********************************************************************
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PulletFramework.Resource;

namespace PulletFramework.Window
{
    /// <summary>
    /// home 键 对应操作
    /// </summary>
    public enum EHomeKeyOperate
    {
        None,           //无处理
        CloseSelf,      //关闭自己
        QuitPopup       //弹出对出弹窗
    }
    /// <summary>
    /// UI窗口基类
    /// 生命周期 OnCreate 、OnOpen、OnUpdate、OnClose、OnDestroy
    /// 辅助周期 OnSetVisible
    /// </summary>
    public abstract class UIWindow
    {
        internal IResourceAssetHandle assetHandle { private set; get; }
        internal GameObject resAsset { private set; get; }
        private System.Action<UIWindow> mPrepareCallback;
        private Action<UIWindow, string> mLoadFailedCallback;
        private Action<UIWindow> mOpenCallBack;
        private System.Object[] mUserDatas;

        private bool mIsCreate = false;
        private bool mIsLoading = false;
        public bool isCreate { get { return mIsCreate; } }

        private GameObject mPanel;
        private Canvas mCanvas;
        private Canvas[] mChildCanvas;
        private CanvasGroup mCanvasGroup;
        private IUIWindowTransition mTransition;
        private bool mGlobalInputAllowed = true;


        /// <summary>
        /// 面板的Transfrom组件
        /// </summary>
        public Transform transform
        {
            get
            {
                return mPanel.transform;
            }
        }

        /// <summary>
        /// 面板的游戏对象
        /// </summary>
        public GameObject gameObject
        {
            get
            {
                return mPanel;
            }
        }

        /// <summary>
        /// 自定义数据
        /// </summary>
        public System.Object userData
        {
            get
            {
                if (mUserDatas != null && mUserDatas.Length >= 1)
                    return mUserDatas[0];
                else
                    return null;
            }
        }

        /// <summary>
        /// 自定义数据集
        /// </summary>
        public System.Object[] userDatas
        {
            get { return mUserDatas; }
        }

        /// <summary>
        /// 窗口名称
        /// </summary>
        public string WindowName { private set; get; }

        /// <summary>
        ///  窗口层级
        /// </summary>
        public EWindowLayer WindowLayer { internal set; get; }

        /// <summary>
        /// 是否为全屏窗口
        /// </summary>
        public bool FullScreen { private set; get; }

        /// <summary>
        /// 窗口显示时是否隐藏常驻界面。
        /// </summary>
        public bool HidePersistent { private set; get; }

        /// <summary>
        /// 窗口关闭后的资源保留策略。
        /// </summary>
        public EWindowCachePolicy CachePolicy { private set; get; }

        /// <summary>
        /// 最近一次资源加载错误。
        /// </summary>
        public string LoadError { private set; get; }

        public bool IsTransitioning { private set; get; }
        public bool IsClosing { private set; get; }
        internal bool IsOpenCompleted { private set; get; }

        /// <summary>
        /// 窗口深度值
        /// </summary>
        public int Depth
        {
            get
            {
                if (mCanvas != null)
                    return mCanvas.sortingOrder;
                else
                    return 0;
            }

            set
            {
                if (mCanvas != null)
                {
                    if (mCanvas.sortingOrder == value)
                        return;

                    // 设置父类
                    mCanvas.sortingOrder = value;

                    // 设置子类
                    int depth = value;
                    for (int i = 0; i < mChildCanvas.Length; i++)
                    {
                        var canvas = mChildCanvas[i];
                        if (canvas != mCanvas)
                        {
                            depth += 5; //注意递增值
                            canvas.sortingOrder = depth;
                        }
                    }

                    // 虚函数
                    if (mIsCreate)
                        OnSortDepth(value);
                }
            }
        }

        /// <summary>
        /// <summary>
        /// 窗口可见性
        /// </summary>
        private bool visible = false;
        public bool Visible
        {
            get
            {
                return visible;
            }

            set
            {
                if (visible == value) return;
                visible = value;
                if (mCanvasGroup != null)
                {
                    mCanvasGroup.alpha = visible ? 1 : 0;
                    RefreshInteractable();

                    // 虚函数
                    if (mIsCreate)
                    {
                        OnSetVisible(visible);
                        if (visible)
                            OnResume();
                        else
                            OnPause();
                    }
                }
            }
        }

        /// <summary>
        /// 窗口交互性
        /// </summary>
        private bool Interactable
        {
            get
            {
                if (mCanvasGroup != null)
                    return mCanvasGroup.interactable;
                else
                    return false;
            }

            set
            {
                if (mCanvasGroup != null)
                {
                    mCanvasGroup.interactable = value;
                    mCanvasGroup.blocksRaycasts = value;
                }
            }
        }

        /// <summary>
        /// 是否加载完毕
        /// </summary>
        internal bool IsLoadDone
        {
            get
            {
                if (isResources)
                {
                    return resAsset != null || !string.IsNullOrEmpty(LoadError);
                }
                else
                {
                    return assetHandle != null && assetHandle.IsDone;
                }
            }
        }

        internal bool IsLoading => mIsLoading;

        /// <summary>
        /// 是否准备完毕
        /// </summary>
        internal bool IsPrepare { private set; get; }



        /// <summary>
        /// 资源包名声
        /// </summary>
        public virtual string packageName => string.Empty;

        /// <summary>
        /// 是否在 Resources 下
        /// </summary>
        public virtual bool isResources { get; set; } = false;

        /// <summary>
        /// 界面资源完整路径
        /// </summary>
        public abstract string assetPath { get; }

        /// <summary>
        /// res 路径下资源路径
        /// </summary>
        public virtual string resAssetPath { get; }

        /// <summary>
        /// 该界面是否响应 home 退出
        /// </summary>
        private EHomeKeyOperate _HomeKeyOperate = EHomeKeyOperate.CloseSelf;
        public EHomeKeyOperate homeKeyOperate
        {
            get { return _HomeKeyOperate; }
            set { _HomeKeyOperate = value; }
        }

        #region 抽象函数和虚函数
        /// <summary>
        /// 窗口创建
        /// </summary>
        public virtual void OnCreate() { }

        /// <summary>
        /// 打开窗口
        /// </summary>
        /// <param name="param"></param>
        protected virtual void OnOpen() { }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        /// <param name="destroy"></param>
        protected virtual void OnClose(bool destroy = false) { }

        /// <summary>
        /// 窗口更新
        /// </summary>
        public virtual void OnUpdate() { }

        /// <summary>
        /// 窗口销毁
        /// </summary>
        public virtual void OnDestroy() { }

        /// <summary>
        /// 窗口从遮挡或关闭状态恢复可见。
        /// </summary>
        protected virtual void OnResume() { }

        /// <summary>
        /// 窗口被全屏窗口遮挡或关闭。
        /// </summary>
        protected virtual void OnPause() { }

        /// <summary>
        /// 返回 false 可阻止返回、Tab 切换等关闭操作。
        /// </summary>
        protected virtual bool CanClose(EWindowCloseReason reason) => true;

        /// <summary>
        /// 当触发窗口的层级排序
        /// </summary>
        protected virtual void OnSortDepth(int depth) { }

        /// <summary>
        /// 当因为全屏遮挡触发窗口的显隐
        /// </summary>
        protected virtual void OnSetVisible(bool visible) { }

        /// <summary>
        /// 平台返回键回调。默认执行 homeKeyOperate，业务窗口可重写实现暂停等自定义行为。
        /// </summary>
        protected virtual void OnHomeDisposeCallBack()
        {
            switch (homeKeyOperate)
            {
                case EHomeKeyOperate.None:
                    break;
                case EHomeKeyOperate.CloseSelf:
                    CloseSelf();
                    break;
                case EHomeKeyOperate.QuitPopup:
                    PulletWindow.InternalOpenQuitPopup();
                    break;
                default:
                    break;
            }
        }
        #endregion

        #region 内部方法
        /// <summary>
        /// 关闭自己
        /// </summary>
        /// <param name="destroy">是否销毁</param>
        protected void CloseSelf(bool destroy = false)
        {
            //关闭自己
            PulletWindow.CloseWindow(this.GetType(), destroy);
        }

        /// <summary>
        /// 关闭窗口并向 OpenWindowForResultAsync 的调用方提交结果。
        /// </summary>
        protected bool CloseWithResult<TResult>(TResult result, bool destroy = false)
        {
            return PulletWindow.CloseWindowWithResult(this, result, destroy);
        }

        private void Handle_Completed(IResourceAssetHandle handle)
        {
            if (!mIsLoading)
                return;
            if (!handle.IsSucceeded || !(handle.AssetObject is GameObject))
            {
                InternalLoadFailed(string.IsNullOrEmpty(handle.Error)
                    ? $"Window asset is not a GameObject: {assetPath}"
                    : $"Asset load failed: {assetPath}，{handle.Error}");
                return;
            }
            try
            {
                PLogger.DebugLog("[Instantiate Window] " + WindowName);
                var options = new ResourceInstantiateOptions(true, PulletWindow.desktop.transform);
                PreparePanel(handle.InstantiateSync(options));
            }
            catch (Exception exception)
            {
                InternalLoadFailed(exception.Message);
            }
        }

        /// <summary>
        /// 获取指定位置的强类型打开参数。
        /// </summary>
        protected T GetUserData<T>(int index = 0)
        {
            if (mUserDatas == null || index < 0 || index >= mUserDatas.Length)
                return default(T);
            return mUserDatas[index] is T value ? value : default(T);
        }

        private void ResAsset_Completed(GameObject assetObject)
        {
            if (assetObject == null)
            {
                InternalLoadFailed($"Resources asset not found: {resAssetPath}");
                return;
            }
            try
            {
                PLogger.DebugLog("[Instantiate Window] " + WindowName);
                PreparePanel(GameObject.Instantiate(assetObject, PulletWindow.desktop.transform));
            }
            catch (Exception exception)
            {
                InternalLoadFailed(exception.Message);
            }
        }

        private void PreparePanel(GameObject panel)
        {
            mPanel = panel;
            if (mPanel == null)
                throw new Exception($"Instantiate window failed: {WindowName}");
            if (!mPanel.activeSelf)
                mPanel.SetActive(true);
            Transform panelTransform = mPanel.transform;
            panelTransform.localPosition = Vector3.zero;
            panelTransform.localRotation = Quaternion.identity;
            panelTransform.localScale = Vector3.one;
            if (panelTransform is RectTransform panelRect)
            {
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
            }

            mCanvas = mPanel.GetComponent<Canvas>();
            if (mCanvas == null)
                throw new Exception($"Not found {nameof(Canvas)} in panel {WindowName}");
            mCanvas.overrideSorting = true;
            mCanvas.sortingOrder = 0;
            mCanvas.sortingLayerName = "Default";

            mChildCanvas = mPanel.GetComponentsInChildren<Canvas>(true);
            mCanvasGroup = mPanel.GetComponent<CanvasGroup>();
            if (mCanvasGroup == null)
                mCanvasGroup = mPanel.AddComponent<CanvasGroup>();

            MonoBehaviour[] behaviours = mPanel.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IUIWindowTransition transition)
                {
                    mTransition = transition;
                    break;
                }
            }

            mIsLoading = false;
            IsPrepare = true;
            mPrepareCallback?.Invoke(this);
            mPrepareCallback = null;
            mLoadFailedCallback = null;
        }
        #endregion

        #region 内部 调用

        internal void Init(
            string name,
            EWindowLayer windowLayer,
            bool fullScreen = true,
            bool hidePersistent = false,
            EWindowCachePolicy cachePolicy = EWindowCachePolicy.Cache)
        {
            WindowName = name;
            this.WindowLayer = windowLayer;
            this.FullScreen = fullScreen;
            this.HidePersistent = hidePersistent;
            this.CachePolicy = cachePolicy;
        }

        internal void InternalLoad(
            System.Action<UIWindow> prepareCallback,
            Action<UIWindow, string> loadFailedCallback,
            System.Object[] userDatas,
            Action<UIWindow> openCallBack)
        {
            mUserDatas = userDatas;
            mOpenCallBack = openCallBack;
            mLoadFailedCallback = loadFailedCallback;
            LoadError = null;
            mIsLoading = true;
            IsOpenCompleted = false;
            IsClosing = false;
            if (isResources)
            {
                string targetPackageName = string.IsNullOrEmpty(packageName) ? null : packageName;
                if (PulletResources.IsConfigured
                    && PulletResources.TryGetPackage(targetPackageName, out IResourcePackage candidatePackage)
                    && candidatePackage.Status == EResourcePackageStatus.Succeeded
                    && candidatePackage.IsLocationValid(assetPath))
                {
                    PLogger.DebugLog($"检测到 ab 存在该资源:{assetPath},改变加载策略");
                    isResources = false;
                }
                else
                {
                    //res 资源
                    if (resAsset)
                    {
                        mIsLoading = false;
                        prepareCallback?.Invoke(this);
                        mLoadFailedCallback = null;
                    }
                    else
                    {
                        mPrepareCallback = prepareCallback;
                        resAsset = Resources.Load<GameObject>(resAssetPath);
                        ResAsset_Completed(resAsset);
                    }
                    return;
                }
            }

            //AB 资源
            if (assetHandle != null)
            {
                mIsLoading = false;
                prepareCallback?.Invoke(this);
                mLoadFailedCallback = null;
            }
            else
            {
                mPrepareCallback = prepareCallback;
                if (!PulletResources.TryGetPackage(packageName, out IResourcePackage package))
                {
                    InternalLoadFailed($"Resource package not found: {packageName}");
                    return;
                }
                if (package.Status != EResourcePackageStatus.Succeeded)
                {
                    InternalLoadFailed($"Resource package is not ready: {package.Name}，{package.Error}");
                    return;
                }
                try
                {
                    assetHandle = package.LoadAssetAsync<GameObject>(assetPath);
                    assetHandle.Completed += Handle_Completed;
                }
                catch (Exception exception)
                {
                    InternalLoadFailed(exception.Message);
                }
            }
        }

        internal void WaitForLoadComplete()
        {
            if (assetHandle != null && !assetHandle.IsDone)
                assetHandle.WaitForCompletion();
            if (IsTransitioning)
                mTransition?.CompleteImmediately();
        }

        internal void InternalCancelLoad()
        {
            if (!mIsLoading)
                return;
            LoadError = $"Window load cancelled: {WindowName}";
            mIsLoading = false;
            mPrepareCallback = null;
            mLoadFailedCallback = null;
            mOpenCallBack = null;
            ReleaseAssetHandle();
        }

        internal bool InternalCanClose(EWindowCloseReason reason)
        {
            if (IsClosing)
                return false;
            try
            {
                return CanClose(reason);
            }
            catch (Exception exception)
            {
                PLogger.Error($"[Window CanClose Failed] {WindowName}: {exception.Message}");
                return false;
            }
        }

        internal void InternalMarkOpenFailed(string error)
        {
            LoadError = string.IsNullOrEmpty(error) ? $"Window open failed: {WindowName}" : error;
        }

        private void InternalLoadFailed(string error)
        {
            mIsLoading = false;
            LoadError = string.IsNullOrEmpty(error) ? $"Window load failed: {WindowName}" : error;
            PLogger.Error($"[Window Load Failed] {WindowName}: {LoadError}");
            mPrepareCallback = null;
            Action<UIWindow, string> failedCallback = mLoadFailedCallback;
            mLoadFailedCallback = null;
            mOpenCallBack = null;
            ReleaseAssetHandle();
            failedCallback?.Invoke(this, LoadError);
        }

        internal void InternalCreate()
        {
            if (mIsCreate == false)
            {
                PLogger.DebugLog("[Create Window] " + WindowName);
                mIsCreate = true;
                OnCreate();
            }
        }

        internal void InternalOpen(Action completed)
        {
            PLogger.DebugLog("[Open Window] " + WindowName);
            OnOpen();
            bool completionHandled = false;

            void CompleteOpen()
            {
                if (completionHandled || !IsPrepare)
                    return;
                completionHandled = true;
                IsTransitioning = false;
                IsOpenCompleted = true;
                RefreshInteractable();
                mOpenCallBack?.Invoke(this);
                mOpenCallBack = null;
                completed?.Invoke();
            }

            if (mTransition == null)
            {
                CompleteOpen();
                return;
            }

            IsTransitioning = true;
            RefreshInteractable();
            try
            {
                mTransition.PlayEnter(CompleteOpen);
            }
            catch (Exception exception)
            {
                PLogger.Error($"[Window Enter Transition Failed] {WindowName}: {exception.Message}");
                CompleteOpen();
            }
        }

        internal void InternalClose(EWindowCloseReason reason, bool destroy, Action completed)
        {
            PLogger.DebugLog("[Close Window] " + WindowName);
            IsClosing = true;
            bool completionHandled = false;

            void CompleteClose()
            {
                if (completionHandled || !IsPrepare)
                    return;
                completionHandled = true;
                IsTransitioning = false;
                Visible = false;
                try
                {
                    OnClose(destroy);
                }
                catch (Exception exception)
                {
                    PLogger.Error($"[Window Close Failed] {WindowName}: {exception.Message}");
                }
                finally
                {
                    IsClosing = false;
                    IsOpenCompleted = false;
                    completed?.Invoke();
                }
            }

            if (mTransition == null)
            {
                CompleteClose();
                return;
            }

            IsTransitioning = true;
            RefreshInteractable();
            try
            {
                mTransition.PlayExit(CompleteClose);
            }
            catch (Exception exception)
            {
                PLogger.Error($"[Window Exit Transition Failed] {WindowName}: {exception.Message}");
                CompleteClose();
            }
        }

        internal void InternalSetGlobalInputAllowed(bool allowed)
        {
            mGlobalInputAllowed = allowed;
            RefreshInteractable();
        }

        private void RefreshInteractable()
        {
            Interactable = visible && mGlobalInputAllowed && !IsTransitioning && !IsClosing;
        }

        internal void InternalUpdate()
        {
            if (IsPrepare)
            {
                OnUpdate();
            }
        }

        internal void InternalDestroy()
        {
            PLogger.DebugLog("[Destroy Window] " + WindowName);
            bool wasCreated = mIsCreate;
            mIsCreate = false;

            // 注销回调函数
            mPrepareCallback = null;
            mLoadFailedCallback = null;
            mOpenCallBack = null;
            mUserDatas = null;
            IsPrepare = false;
            mIsLoading = false;
            IsOpenCompleted = false;
            IsClosing = false;
            IsTransitioning = false;

            try
            {
                mTransition?.Cancel();
            }
            catch (Exception exception)
            {
                PLogger.Error($"[Window Transition Cancel Failed] {WindowName}: {exception.Message}");
            }
            mTransition = null;

            // 卸载面板资源
            if (isResources)
            {
                resAsset = null;
            }
            else
            {
                ReleaseAssetHandle();
            }

            // 销毁面板对象
            if (mPanel != null)
            {
                try
                {
                    if (wasCreated)
                        OnDestroy();
                }
                catch (Exception exception)
                {
                    PLogger.Error($"[Window Destroy Failed] {WindowName}: {exception.Message}");
                }
                finally
                {
                    GameObject.Destroy(mPanel);
                    mPanel = null;
                }
            }
        }

        private void ReleaseAssetHandle()
        {
            if (assetHandle == null)
                return;
            assetHandle.Completed -= Handle_Completed;
            assetHandle.Release();
            assetHandle = null;
        }

        /// <summary>
        /// home 键 处理
        /// </summary>
        internal void InternalHomeKeyDispose()
        {
            PLogger.DebugLog("[Window] HomeKeyDispose:" + WindowName);
            OnHomeDisposeCallBack();
        }
        #endregion

        #region Find func

        /// <summary>
        /// 查找GameObject
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        protected GameObject Find(string name)
        {
            return Find(gameObject, name);
        }


        /// <summary>
        /// 查找Transform
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        protected Transform FindTrans(string name)
        {
            return transform.Find(name);
        }

        /// <summary>
        /// 查找指定 obj 子物体
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        protected GameObject Find(GameObject obj, string name)
        {
            Transform trans = FindByName(obj.transform, name);
            if (trans != null)
                return trans.gameObject;
            else
                return null;
        }

        /// <summary>
        /// 查找自身
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected T Find<T>()
        {
            return gameObject.GetComponent<T>();
        }

        protected T Find<T>(GameObject obj)
        {
            return obj.GetComponent<T>();
        }

        protected T Find<T>(string name)
        {
            return FindByTransform<T>(transform, name);
        }

        protected T Find<T>(Transform transform, string name)
        {
            return FindByTransform<T>(transform, name);
        }

        protected T Find<T>(GameObject obj, string name)
        {
            return FindByTransform<T>(obj.transform, name);
        }

        private T FindByTransform<T>(Transform transform, string name)
        {
            Transform trans = FindByName(transform, name);
            if (trans != null)
                return trans.GetComponent<T>();
            return default(T);
        }

        private Transform FindByName(Transform trans, string name)
        {
            if (trans == null)
                return null;

            if (trans.name == name)
                return trans;

            return FindTranformInChild(trans, name);
        }

        private Transform FindTranformInChild(Transform trans, string name)
        {
            if (trans == null)
                return null;

            Transform tempTrans = trans.Find(name);
            if (tempTrans != null)
                return tempTrans;

            for (int i = 0; i < trans.childCount; i++)
            {
                Transform child = trans.GetChild(i);
                Transform temp = FindTranformInChild(child, name);
                if (temp != null)
                    return temp;
            }
            return null;
        }


        #endregion
    }

    /// <summary>
    /// 带强类型打开参数的窗口基类。
    /// </summary>
    public abstract class UIWindow<TArguments> : UIWindow
    {
        protected TArguments Arguments => GetUserData<TArguments>();
    }
}
