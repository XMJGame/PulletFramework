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
using YooAsset;

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
        //public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        //public const int WINDOW_SHOW_LAYER = 5; // UI
        internal AssetHandle assetHandle { private set; get; }
        internal GameObject resAsset { private set; get; }
        private System.Action<UIWindow> mPrepareCallback;
        private Action<UIWindow> mOpenCallBack;
        private System.Object[] mUserDatas;

        private bool mIsCreate = false;
        public bool isCreate { get { return mIsCreate; } }

        private GameObject mPanel;
        private Canvas mCanvas;
        private Canvas[] mChildCanvas;
        //private GraphicRaycaster mRaycaster;
        //private GraphicRaycaster[] mChildRaycaster;
        private CanvasGroup mCanvasGroup;


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
        public EWindowLayer WindowLayer { private set; get; }

        /// <summary>
        /// 是否为全屏窗口
        /// </summary>
        public bool FullScreen { private set; get; }

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
                    // 交互设置
                    Interactable = visible;

                    // 虚函数
                    if (mIsCreate)
                    {
                        OnSetVisible(visible);
                        //PLogger.Log("[OnSetVisible Window] " + WindowName + " Visible:" + visible);
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
                    return resAsset;
                }
                else
                {
                    return assetHandle.IsDone;
                }
            }
        }

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
        public abstract void OnCreate();

        /// <summary>
        /// 打开窗口
        /// </summary>
        /// <param name="param"></param>
        protected abstract void OnOpen();

        /// <summary>
        /// 关闭窗口
        /// </summary>
        /// <param name="destroy"></param>
        protected virtual void OnClose(bool destroy = false) { }

        /// <summary>
        /// 窗口更新
        /// </summary>
        public abstract void OnUpdate();

        /// <summary>
        /// 窗口销毁
        /// </summary>
        public abstract void OnDestroy();

        /// <summary>
        /// 当触发窗口的层级排序
        /// </summary>
        protected virtual void OnSortDepth(int depth) { }

        /// <summary>
        /// 当因为全屏遮挡触发窗口的显隐
        /// </summary>
        protected virtual void OnSetVisible(bool visible) { }

        /// <summary>
        /// home 键 回调
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

        private void Handle_Completed(AssetHandle handle)
        {
            if (handle.AssetObject == null)
                return;
            PLogger.Log("[Instantiate Window] " + WindowName);
            // 实例化对象
            mPanel = handle.InstantiateSync(PulletWindow.desktop.transform);
            if (!mPanel.activeSelf)
                mPanel.SetActive(true);
            mPanel.transform.localPosition = Vector3.zero;
            mPanel.transform.localRotation = Quaternion.identity;

            // 获取组件
            mCanvas = mPanel.GetComponent<Canvas>();
            if (mCanvas == null)
                throw new Exception($"Not found {nameof(Canvas)} in panel {WindowName}");
            mCanvas.overrideSorting = true;
            mCanvas.sortingOrder = 0;
            mCanvas.sortingLayerName = "Default";

            // 获取组件
            mChildCanvas = mPanel.GetComponentsInChildren<Canvas>(true);
            //_Raycaster = mPanel.GetComponent<GraphicRaycaster>();       
            //_ChildRaycaster = mPanel.GetComponentsInChildren<GraphicRaycaster>(true);

            mCanvasGroup = mPanel.GetComponent<CanvasGroup>();
            if (mCanvasGroup == null)
                mCanvasGroup = mPanel.AddComponent<CanvasGroup>();
            //mCanvasGroup.alpha = 0;
            // 通知UI管理器
            IsPrepare = true;
            mPrepareCallback?.Invoke(this);
        }

        private void ResAsset_Completed(GameObject assetObject)
        {
            if (assetObject == null)
            {
                PLogger.Error("[Window Not Asset] " + WindowName);
                return;
            }
            PLogger.Log("[Instantiate Window] " + WindowName);
            // 实例化对象
            mPanel = GameObject.Instantiate(assetObject, PulletWindow.desktop.transform);//handle.InstantiateSync(PulletWindow.desktop.transform);
            if (!mPanel.activeSelf)
                mPanel.SetActive(true);
            mPanel.transform.localPosition = Vector3.zero;
            mPanel.transform.localRotation = Quaternion.identity;

            // 获取组件
            mCanvas = mPanel.GetComponent<Canvas>();
            if (mCanvas == null)
                throw new Exception($"Not found {nameof(Canvas)} in panel {WindowName}");
            mCanvas.overrideSorting = true;
            mCanvas.sortingOrder = 0;
            mCanvas.sortingLayerName = "Default";

            // 获取组件
            mChildCanvas = mPanel.GetComponentsInChildren<Canvas>(true);
            //_Raycaster = mPanel.GetComponent<GraphicRaycaster>();       
            //_ChildRaycaster = mPanel.GetComponentsInChildren<GraphicRaycaster>(true);

            mCanvasGroup = mPanel.GetComponent<CanvasGroup>();
            if (mCanvasGroup == null)
                mCanvasGroup = mPanel.AddComponent<CanvasGroup>();
            //mCanvasGroup.alpha = 0;
            // 通知UI管理器
            IsPrepare = true;
            mPrepareCallback?.Invoke(this);
        }
        #endregion

        #region 内部 调用

        internal void Init(string name, EWindowLayer windowLayer, bool fullScreen = true)
        {
            WindowName = name;
            this.WindowLayer = windowLayer;
            this.FullScreen = fullScreen;
        }

        internal void InternalLoad(System.Action<UIWindow> prepareCallback, System.Object[] userDatas, Action<UIWindow> openCallBack)
        {
            mUserDatas = userDatas;
            mOpenCallBack = openCallBack;
            if (isResources)
            {
                if (YooAssets.CheckLocationValid(assetPath))
                {
                    PLogger.Log($"检测到 ab 存在该资源:{assetPath},改变加载策略");
                    isResources = false;
                }
                else
                {
                    //res 资源
                    if (resAsset)
                    {
                        prepareCallback?.Invoke(this);
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
                prepareCallback?.Invoke(this);
            }
            else
            {
                mPrepareCallback = prepareCallback;
                if (string.IsNullOrEmpty(packageName))
                {
                    assetHandle = YooAssets.LoadAssetAsync<GameObject>(assetPath);
                    assetHandle.Completed += Handle_Completed;
                }
            }
        }

        internal void InternalCreate()
        {
            if (mIsCreate == false)
            {
                PLogger.Log("[Create Window] " + WindowName);
                mIsCreate = true;
                OnCreate();
            }
        }

        internal void InternalOpen()
        {
            PLogger.Log("[Open Window] " + WindowName);
            OnOpen();
            if (mOpenCallBack != null)
            {
                mOpenCallBack.Invoke(this);
                mOpenCallBack = null;
            }
        }

        internal void InternalClose(bool destroy = false)
        {
            PLogger.Log("[Close Window] " + WindowName);
            Visible = false;
            OnClose(destroy);
        }

        //internal void InternalRouse()
        //{
        //    PLogger.Log("[Rouse Window] " + WindowName);
        //    OnRouse();
        //}

        internal void InternalUpdate()
        {
            if (IsPrepare)
            {
                OnUpdate();
            }
        }

        internal void InternalDestroy()
        {
            PLogger.Log("[Destroy Window] " + WindowName);
            mIsCreate = false;

            // 注销回调函数
            mPrepareCallback = null;

            // 卸载面板资源
            if (isResources)
            {
                resAsset = null;
            }
            else
            {
                if (assetHandle != null)
                {
                    assetHandle.Release();
                    assetHandle = null;
                }
            }

            // 销毁面板对象
            if (mPanel != null)
            {
                OnDestroy();
                GameObject.Destroy(mPanel);
                mPanel = null;
            }
        }

        /// <summary>
        /// home 键 处理
        /// </summary>
        internal void InternalHomeKeyDispose()
        {
            PLogger.Log("[Window] HomeKeyDispose:" + WindowName);
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
            //return transform.Find(name).gameObject;
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
}
