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
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PulletFramework.Window
{
    [Serializable]
    public struct WindowInfo
    {
        public string windowName;
        public int windowLayer;
        public bool visible;

        public override string ToString()
        {
            return $"({windowName}, {windowLayer}, {visible})";
        }
    }

    /// <summary>
    /// UI界面层级
    /// </summary>
    public enum EWindowLayer
    {
        SceneLayer = 0,         //场景UI
        BackgroundLayer = 1000, //背景UI，如：场景背景、全屏装饰
        RootLayer = 1500,       //根页面，如：首页、角色、背包等 Tab 页面
        NormalLayer = 2000,     //普通UI，一级、二级、三级等窗口---一般由用户点击打开的多级窗口
        NavigationLayer = 2500, //常驻导航，如：底部 Tab
        PopupLayer = 3000,      //弹窗UI 如：
        TopLayer = 4000,        //顶层UI，如：场景加载
        IndependentLayer = 5000,//独立UI, 只负责创建出来，不负责管理，需要自己管理
    }

    /// <summary>
    /// 界面系统
    /// </summary>
    public static class PulletWindow
    {
        private sealed class WindowResultRegistration
        {
            public Type ResultType;
            public Action<bool, object> Callback;
        }

        private sealed class PopupRequest
        {
            public long Id;
            public Type WindowType;
            public System.Object[] UserDatas;
            public Action<UIWindow> Opened;
            public UIWindow Window;
        }

        public static event Action<UIWindow> WindowOpened;
        public static event Action<UIWindow> WindowClosed;
        public static event Action<string, UIWindow> ActiveTabChanged;

        private static bool m_IsInitialize = false;
        private static bool m_IsClosingAll = false;
        //加载过的所有窗口（打开、关闭的）缓存
        private static readonly Dictionary<Type, UIWindow> m_WindowsCache = new Dictionary<Type, UIWindow>();
        //打开界面栈
        private static readonly List<UIWindow> m_OpenStack = new List<UIWindow>();
        private static readonly Dictionary<UIWindow, long> m_OpenSequence = new Dictionary<UIWindow, long>();
        private static readonly Dictionary<Type, WindowResultRegistration> m_ResultCallbacks =
            new Dictionary<Type, WindowResultRegistration>();
        private static readonly Dictionary<UIWindow, object> m_PendingResults =
            new Dictionary<UIWindow, object>();
        private static readonly Queue<PopupRequest> m_PopupQueue = new Queue<PopupRequest>();
        private static PopupRequest m_ActivePopupRequest;
        private static long m_NextPopupRequestId;
        //打开的界面入栈信息 -- 用来 清理界面缓存后，返回后可以加载记录栈数据
        private static readonly List<Type> m_RecordedWindows = new List<Type>();
        private static long m_NextOpenSequence;
        private static UIWindow m_CurrentRoot;
        private static UIWindow m_PendingRoot;
        private static long m_TabSwitchVersion;

        public static UIWindow CurrentRoot => m_CurrentRoot;
        public static string ActiveTabId { private set; get; }

        private static GameObject m_GameObject;
        public static GameObject gameObject { get { return m_GameObject; } }
        public static Transform transform { get { return m_GameObject.transform; } }

        private static GameObject m_Desktop;
        public static GameObject desktop
        {
            get { return m_Desktop; }
        }

        private static Canvas m_Canvas;
        public static Canvas canvas { get { return m_Canvas; } }

        private static CanvasScaler m_CanvasScaler;
        private static CanvasScaler canvasScaler { get { return m_CanvasScaler; } }

        private static GraphicRaycaster m_GraphicRaycaster;
        private static GraphicRaycaster graphicRaycaster { get { return m_GraphicRaycaster; } }

        private static bool m_HomeKaySwitch = true;
        public static void Initialize(GameObject desktop = null)
        {
            if (m_IsInitialize)
                throw new Exception($"{nameof(PulletWindow)} is initialized !");

            Canvas desktopCanvas = null;
            CanvasScaler desktopScaler = null;
            GraphicRaycaster desktopRaycaster = null;
            if (desktop != null)
            {
                desktopCanvas = desktop.GetComponent<Canvas>();
                desktopScaler = desktop.GetComponent<CanvasScaler>();
                desktopRaycaster = desktop.GetComponent<GraphicRaycaster>();
                if (desktopCanvas == null || desktopScaler == null || desktopRaycaster == null)
                    throw new InvalidOperationException("Canvas、CanvasScaler、GraphicRaycaster 其中有一个或多个组件查找不到");
            }

            // 所有前置校验通过后，再提交初始化状态。
            m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletWindow)}]");
            if (desktop == null)
            {
                m_Desktop = new UnityEngine.GameObject("UICanvas");
                m_Desktop.transform.SetParent(transform);
                m_Desktop.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                m_Canvas = m_Desktop.AddComponent<Canvas>();
                m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                m_CanvasScaler = m_Desktop.AddComponent<CanvasScaler>();
                m_CanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                m_CanvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                m_CanvasScaler.matchWidthOrHeight = 0.5f;
                m_GraphicRaycaster = m_Desktop.AddComponent<GraphicRaycaster>();
            }
            else
            {
                m_Desktop = desktop;
                m_Canvas = desktopCanvas;
                m_CanvasScaler = desktopScaler;
                m_GraphicRaycaster = desktopRaycaster;
            }
            m_IsInitialize = true;
            PLogger.Log($"{nameof(PulletWindow)} initialize !");
        }

        public static void Destroy()
        {
            if (m_IsInitialize)
            {
                m_IsInitialize = false;
                CloseAll();
                if (gameObject != null)
                    GameObject.Destroy(gameObject);
                PLogger.Log($"{nameof(PulletWindow)} destroy all !");
                WindowOpened = null;
                WindowClosed = null;
                ActiveTabChanged = null;
            }
        }

        /// <summary>
		/// 更新界面系统
		/// </summary>
        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (m_IsInitialize)
            {
                int count = m_OpenStack.Count;
                for (int i = 0; i < m_OpenStack.Count; i++)
                {
                    if (m_OpenStack.Count != count)
                        break;
                    var window = m_OpenStack[i];
                    if (window.Visible)
                        window.InternalUpdate();
                }

#if UNITY_ANDROID || UNITY_EDITOR
                //安卓上面监听 home 键
                if (m_HomeKaySwitch && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Home)))
                {
                    //返回当前顶部界面
                    if (m_OpenStack.Count > 0)
                    {
                        UIWindow window = GetTopWindow(true);
                        if (window != null)
                        {
                            window.InternalHomeKeyDispose();
                        }
                    }
                }
#endif

            }
        }

        /// <summary>
        /// 设置窗口
        /// </summary>
        /// <param name="vector"></param>
        public static void SetReferenceResolution(Vector2 vector)
        {
            if (m_CanvasScaler)
            {
                m_CanvasScaler.referenceResolution = vector;
            }
        }

        /// <summary>
        /// 设置是有启用 home 键 开关
        /// 用来响应界面返回，弹窗提示退出游戏
        /// </summary>
        /// <param name="on"></param>
        public static void SetHomeKeySwitch(bool on)
        {
            m_HomeKaySwitch = on;
        }

        private static Type mQuitPopupWindow;
        private static Action<UIWindow> mQuitPopupCallBack;
        public static void SetHomeQuitPopupWindow<T>(Action<UIWindow> callBack)
        {
            mQuitPopupWindow = typeof(T);
            mQuitPopupCallBack = callBack;
        }

        /// <summary>
		/// 获取窗口堆栈信息
		/// </summary>
		public static void GetWindowInfos(List<WindowInfo> output)
        {
            if (output == null)
                output = new List<WindowInfo>();
            else
                output.Clear();

            for (int i = 0; i < m_OpenStack.Count; i++)
            {
                var window = m_OpenStack[i];
                WindowInfo info = new WindowInfo();
                info.windowName = window.WindowName;
                info.windowLayer = (int)window.WindowLayer;
                info.visible = window.Visible;
                output.Add(info);
            }
        }

        /// <summary>
        /// 异步打开窗口
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="userDatas">用户自定义数据</param>
        public static OpenWindowOperation OpenWindowAsync<T>(params System.Object[] userDatas) where T : UIWindow
        {
            return OpenWindowAsync(typeof(T), null, null, userDatas);
        }
        public static OpenWindowOperation OpenWindowAsync<T>(Action<UIWindow> openCallBack, params System.Object[] userDatas) where T : UIWindow
        {
            return OpenWindowAsync(typeof(T), openCallBack, null, userDatas);
        }

        public static OpenWindowOperation OpenWindowAsync<TWindow, TArguments>(TArguments arguments)
            where TWindow : UIWindow<TArguments>
        {
            return OpenWindowAsync(typeof(TWindow), null, null, arguments);
        }

        /// <summary>
        /// 打开窗口并在其关闭动画完成后接收结果。
        /// </summary>
        public static OpenWindowOperation OpenWindowForResultAsync<TWindow, TResult>(
            Action<UIWindowResult<TResult>> completed,
            params System.Object[] userDatas)
            where TWindow : UIWindow
        {
            Type type = typeof(TWindow);
            if (m_ResultCallbacks.ContainsKey(type))
                return CreateFailedOperation($"Window is already waiting for a result: {type.FullName}");

            m_ResultCallbacks[type] = new WindowResultRegistration
            {
                ResultType = typeof(TResult),
                Callback = (hasValue, value) => completed?.Invoke(
                    new UIWindowResult<TResult>(hasValue, hasValue ? (TResult)value : default(TResult))),
            };

            try
            {
                OpenWindowOperation operation = OpenWindowAsync(type, null, null, userDatas);
                if (operation.Window == null)
                    m_ResultCallbacks.Remove(type);
                return operation;
            }
            catch
            {
                m_ResultCallbacks.Remove(type);
                throw;
            }
        }

        /// <summary>
        /// 打开详情页并压入返回栈。
        /// </summary>
        public static OpenWindowOperation PushAsync<T>(params System.Object[] userDatas) where T : UIWindow
        {
            return OpenWindowAsync(typeof(T), null, EWindowLayer.NormalLayer, userDatas);
        }

        public static OpenWindowOperation PushAsync<TWindow, TArguments>(TArguments arguments)
            where TWindow : UIWindow<TArguments>
        {
            return OpenWindowAsync(typeof(TWindow), null, EWindowLayer.NormalLayer, arguments);
        }

        /// <summary>
        /// 打开弹窗。
        /// </summary>
        public static OpenWindowOperation ShowPopupAsync<T>(params System.Object[] userDatas) where T : UIWindow
        {
            return OpenWindowAsync(typeof(T), null, EWindowLayer.PopupLayer, userDatas);
        }

        public static OpenWindowOperation ShowPopupAsync<TWindow, TArguments>(TArguments arguments)
            where TWindow : UIWindow<TArguments>
        {
            return OpenWindowAsync(typeof(TWindow), null, EWindowLayer.PopupLayer, arguments);
        }

        /// <summary>
        /// 将弹窗加入串行队列。队列会等待当前 PopupLayer 窗口关闭。
        /// </summary>
        public static long EnqueuePopup<T>(params System.Object[] userDatas)
            where T : UIWindow
        {
            return EnqueuePopup<T>(null, userDatas);
        }

        public static long EnqueuePopup<T>(Action<UIWindow> opened, params System.Object[] userDatas)
            where T : UIWindow
        {
            var request = new PopupRequest
            {
                Id = ++m_NextPopupRequestId,
                WindowType = typeof(T),
                UserDatas = userDatas,
                Opened = opened,
            };
            m_PopupQueue.Enqueue(request);
            TryOpenNextPopup();
            return request.Id;
        }

        public static int QueuedPopupCount => m_PopupQueue.Count + (m_ActivePopupRequest == null ? 0 : 1);

        /// <summary>
        /// 取消尚未显示的队列弹窗。
        /// </summary>
        public static bool CancelQueuedPopup(long requestId)
        {
            if (m_PopupQueue.Count == 0)
                return false;

            bool removed = false;
            var retained = new Queue<PopupRequest>(m_PopupQueue.Count);
            while (m_PopupQueue.Count > 0)
            {
                PopupRequest request = m_PopupQueue.Dequeue();
                if (!removed && request.Id == requestId)
                    removed = true;
                else
                    retained.Enqueue(request);
            }
            while (retained.Count > 0)
                m_PopupQueue.Enqueue(retained.Dequeue());
            return removed;
        }

        public static void ClearPopupQueue()
        {
            m_PopupQueue.Clear();
        }

        /// <summary>
        /// 打开底部导航、网络状态等常驻窗口。
        /// </summary>
        public static OpenWindowOperation ShowPersistentAsync<T>(params System.Object[] userDatas) where T : UIWindow
        {
            return OpenWindowAsync(typeof(T), null, EWindowLayer.NavigationLayer, userDatas);
        }

        /// <summary>
        /// 切换根页面。Tab 切换不会写入返回栈。
        /// </summary>
        public static OpenWindowOperation SwitchTabAsync<T>(string tabId, params System.Object[] userDatas) where T : UIWindow
        {
            Type type = typeof(T);
            if (m_CurrentRoot != null
                && m_CurrentRoot.GetType() != type
                && !m_CurrentRoot.InternalCanClose(EWindowCloseReason.Navigation))
                return CreateFailedOperation($"Window refused navigation: {m_CurrentRoot.WindowName}");

            if (!CloseNavigationOverlays())
                return CreateFailedOperation("当前窗口阻止了 Tab 切换。");
            if (m_CurrentRoot != null
                && m_CurrentRoot.GetType() == type
                && m_OpenStack.Contains(m_CurrentRoot))
            {
                if (m_CurrentRoot.IsClosing)
                    return CreateFailedOperation($"Window is closing: {m_CurrentRoot.WindowName}");
                ActiveTabId = tabId;
                ActiveTabChanged?.Invoke(tabId, m_CurrentRoot);
                var currentOperation = new OpenWindowOperation(m_CurrentRoot);
                PulletOperationSystem.Start(currentOperation);
                return currentOperation;
            }

            if (m_PendingRoot != null && m_PendingRoot.GetType() == type && m_PendingRoot.IsLoading)
            {
                var pendingOperation = new OpenWindowOperation(m_PendingRoot);
                PulletOperationSystem.Start(pendingOperation);
                return pendingOperation;
            }

            if (m_PendingRoot != null && m_PendingRoot.IsLoading)
                CloseWindowInternal(m_PendingRoot, true, EWindowCloseReason.Navigation, true);

            UIWindow previousRoot = m_CurrentRoot;
            long switchVersion = ++m_TabSwitchVersion;
            OpenWindowOperation operation = OpenWindowAsync(
                type,
                window => CommitTabSwitch(switchVersion, tabId, window, previousRoot),
                EWindowLayer.RootLayer,
                userDatas);
            if (operation.Window != null && operation.Window.IsLoading)
                m_PendingRoot = operation.Window;
            return operation;
        }

        public static OpenWindowOperation SwitchTabAsync<TWindow, TArguments>(
            string tabId,
            TArguments arguments)
            where TWindow : UIWindow<TArguments>
        {
            return SwitchTabAsync<TWindow>(tabId, arguments);
        }

        private static void CommitTabSwitch(long switchVersion, string tabId, UIWindow window, UIWindow previousRoot)
        {
            if (switchVersion != m_TabSwitchVersion)
            {
                if (m_OpenStack.Contains(window))
                    CloseWindowInternal(window, false, EWindowCloseReason.Navigation, true);
                return;
            }

            if (m_PendingRoot == window)
                m_PendingRoot = null;
            if (previousRoot != null && previousRoot != window && m_OpenStack.Contains(previousRoot))
                CloseWindowInternal(previousRoot, false, EWindowCloseReason.Navigation, true);

            ActiveTabId = tabId;
            m_CurrentRoot = window;
            ActiveTabChanged?.Invoke(tabId, window);
        }

        private static bool CloseNavigationOverlays()
        {
            var opened = new List<UIWindow>(m_OpenStack);
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                UIWindow window = opened[i];
                if ((window.WindowLayer == EWindowLayer.NormalLayer
                    || window.WindowLayer == EWindowLayer.PopupLayer
                    || window.WindowLayer == EWindowLayer.TopLayer)
                    && !window.InternalCanClose(EWindowCloseReason.Navigation))
                    return false;
            }

            for (int i = opened.Count - 1; i >= 0; i--)
            {
                UIWindow window = opened[i];
                if (window.WindowLayer == EWindowLayer.NormalLayer
                    || window.WindowLayer == EWindowLayer.PopupLayer
                    || window.WindowLayer == EWindowLayer.TopLayer)
                    CloseWindowInternal(window, false, EWindowCloseReason.Navigation, true);
            }
            return true;
        }

        private static OpenWindowOperation CreateFailedOperation(string error)
        {
            var operation = new OpenWindowOperation(null, error);
            PulletOperationSystem.Start(operation);
            return operation;
        }

        private static OpenWindowOperation OpenWindowAsync(
            Type type,
            Action<UIWindow> openCallBack,
            EWindowLayer? layerOverride,
            params System.Object[] userDatas)
        {
            if (!m_IsInitialize)
                throw new InvalidOperationException("PulletWindow 尚未初始化。");
            if (m_IsClosingAll)
                throw new InvalidOperationException("PulletWindow 正在关闭全部窗口，暂时不能打开新窗口。");
            if (type == null || !typeof(UIWindow).IsAssignableFrom(type))
                throw new ArgumentException("窗口类型必须继承 UIWindow。", nameof(type));

            if (!m_WindowsCache.TryGetValue(type, out UIWindow window))
            {
                window = CreateInstance(type);
                m_WindowsCache.Add(type, window);
            }

            if (layerOverride.HasValue)
                window.WindowLayer = layerOverride.Value;

            if (window.IsClosing)
                return CreateFailedOperation($"Window is closing: {window.WindowName}");

            if (window.Visible || window.IsLoading)
            {
                var existingOperation = new OpenWindowOperation(window);
                PulletOperationSystem.Start(existingOperation);
                return existingOperation;
            }

            OpenWindowOperation operation = new OpenWindowOperation(window, ownsOpenAttempt: true);
            PulletOperationSystem.Start(operation);

            if (window.WindowLayer != EWindowLayer.IndependentLayer)
            {
                if (m_OpenStack.Contains(window))
                    m_OpenStack.Remove(window);
                m_OpenStack.Add(window);
                m_OpenSequence[window] = ++m_NextOpenSequence;
            }

            window.InternalLoad(OnWindowPrepare, OnWindowLoadFailed, userDatas, openCallBack);
            return operation;
        }

        /// <summary>
        /// 同步打开窗口
        /// </summary>
        /// <typeparam name="T">窗口类</typeparam>
        /// <param name="userDatas">用户自定义数据</param>
        public static OpenWindowOperation OpenWindowSync<T>(params System.Object[] userDatas) where T : UIWindow
        {
            var operation = OpenWindowAsync(typeof(T), null, null, userDatas);
            operation.WaitForAsyncComplete();
            return operation;
        }
        public static OpenWindowOperation OpenWindowSync(Type type, params System.Object[] userDatas)
        {
            var operation = OpenWindowAsync(type, null, null, userDatas);
            operation.WaitForAsyncComplete();
            return operation;
        }

        /// <summary>
        /// 获取窗口是否打开
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static bool GetWindowIsOpen<T>() where T : UIWindow
        {
            return m_WindowsCache.TryGetValue(typeof(T), out UIWindow window)
                && m_OpenStack.Contains(window);
        }

        /// <summary>
		/// 关闭窗口
		/// </summary>
		public static void CloseWindow<T>(bool destroy = false) where T : UIWindow
        {
            TryCloseWindow(typeof(T), EWindowCloseReason.User, destroy);
        }
        public static void CloseWindow(Type type, bool destroy = false)
        {
            TryCloseWindow(type, EWindowCloseReason.User, destroy);
        }

        /// <summary>
        /// 尝试关闭窗口。返回 false 表示窗口不存在、正在关闭或被窗口拦截。
        /// </summary>
        public static bool TryCloseWindow<T>(EWindowCloseReason reason = EWindowCloseReason.User, bool destroy = false)
            where T : UIWindow
        {
            return TryCloseWindow(typeof(T), reason, destroy);
        }

        public static bool TryCloseWindow(Type type, EWindowCloseReason reason, bool destroy = false)
        {
            if (!m_WindowsCache.TryGetValue(type, out UIWindow window) || !m_OpenStack.Contains(window))
                return false;
            return CloseWindowInternal(window, destroy, reason);
        }

        /// <summary>
        /// 关闭最上层弹窗或详情页。Root 和 Persistent 不会被 Pop。
        /// </summary>
        public static bool Pop(bool destroy = false)
        {
            UIWindow top = GetTopWindow(false);
            if (top == null || top == m_CurrentRoot || top.WindowLayer == EWindowLayer.NavigationLayer)
                return false;
            return CloseWindowInternal(top, destroy, EWindowCloseReason.Back);
        }

        /// <summary>
        /// 关闭当前 Tab 上的详情页、弹窗和顶层窗口，返回根页面。
        /// </summary>
        public static void PopToRoot(bool destroy = false)
        {
            TryPopToRoot(destroy);
        }

        public static bool TryPopToRoot(bool destroy = false)
        {
            var targets = new List<UIWindow>();
            for (int i = m_OpenStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = m_OpenStack[i];
                if (window == m_CurrentRoot || window.WindowLayer == EWindowLayer.NavigationLayer)
                    continue;
                if (window.WindowLayer == EWindowLayer.NormalLayer
                    || window.WindowLayer == EWindowLayer.PopupLayer
                    || window.WindowLayer == EWindowLayer.TopLayer)
                {
                    if (!window.InternalCanClose(EWindowCloseReason.Back))
                        return false;
                    targets.Add(window);
                }
            }

            for (int i = 0; i < targets.Count; i++)
                CloseWindowInternal(targets[i], destroy, EWindowCloseReason.Back, true);
            return true;
        }

        /// <summary>
        /// 释放所有已关闭且允许回收的缓存窗口。
        /// </summary>
        public static int TrimCache(bool includePermanent = false)
        {
            var cached = new List<UIWindow>(m_WindowsCache.Values);
            int released = 0;
            for (int i = 0; i < cached.Count; i++)
            {
                UIWindow window = cached[i];
                if (m_OpenStack.Contains(window))
                    continue;
                if (!includePermanent && window.CachePolicy == EWindowCachePolicy.Permanent)
                    continue;

                m_WindowsCache.Remove(window.GetType());
                window.InternalDestroy();
                released++;
            }
            return released;
        }

        /// <summary>
        /// 判断窗口是否是当前最上层可交互窗口。
        /// </summary>
        public static bool IsTop<T>() where T : UIWindow
        {
            return GetTopWindow(true) is T;
        }

        /// <summary>
        /// 关闭所有窗口
        /// </summary>
        public static void CloseAll(bool isRecord = false)
        {
            PLogger.DebugLog("清理所有界面");
            m_IsClosingAll = true;
            try
            {
                m_RecordedWindows.Clear();
                if (isRecord)
                {
                    List<UIWindow> sorted = GetVisualOrder();
                    for (int i = 0; i < sorted.Count; i++)
                        m_RecordedWindows.Add(sorted[i].GetType());
                }

                var cached = new List<UIWindow>(m_WindowsCache.Values);
                m_WindowsCache.Clear();
                m_OpenStack.Clear();
                m_OpenSequence.Clear();
                m_PendingResults.Clear();
                m_PopupQueue.Clear();
                m_ActivePopupRequest = null;
                m_CurrentRoot = null;
                m_PendingRoot = null;
                m_TabSwitchVersion++;
                ActiveTabId = null;

                for (int i = 0; i < cached.Count; i++)
                {
                    CompleteWindowResult(cached[i].GetType(), false, null);
                    cached[i].InternalDestroy();
                }
                m_ResultCallbacks.Clear();
            }
            finally
            {
                m_IsClosingAll = false;
            }
        }

        /// <summary>
        /// 恢复最近一次 CloseAll(true) 记录的窗口。打开参数需由窗口自行恢复。
        /// </summary>
        public static IReadOnlyList<OpenWindowOperation> RestoreRecordedWindows()
        {
            var operations = new List<OpenWindowOperation>(m_RecordedWindows.Count);
            for (int i = 0; i < m_RecordedWindows.Count; i++)
                operations.Add(OpenWindowAsync(m_RecordedWindows[i], null, null));
            return operations;
        }


        /// <summary>
        /// 获取窗口
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static UIWindow GetCacheWindow<T>() where T : UIWindow
        {
            m_WindowsCache.TryGetValue(typeof(T), out UIWindow window);
            return window;
        }


        /// <summary>
        /// 打开退出弹窗
        /// </summary>
        internal static void InternalOpenQuitPopup()
        {
            if (mQuitPopupWindow == null)
            {
                PLogger.Warning("尚未设置退出确认窗口。");
                return;
            }
            OpenWindowAsync(mQuitPopupWindow, mQuitPopupCallBack, EWindowLayer.PopupLayer);
        }
        static List<WindowInfo> outinfo = new List<WindowInfo>();
        private static void OnWindowPrepare(UIWindow window)
        {
            try
            {
                window.InternalCreate();
                OnSortWindowDepth();
                OnSetWindowVisible();
                window.InternalOpen(() =>
                {
                    OnSetWindowVisible();
                    WindowOpened?.Invoke(window);
                });
                RefreshWindowInteraction();
            }
            catch (Exception exception)
            {
                window.InternalMarkOpenFailed(exception.Message);
                OnWindowLoadFailed(window, exception.Message);
            }
        }

        private static void OnWindowLoadFailed(UIWindow window, string error)
        {
            m_OpenStack.Remove(window);
            m_OpenSequence.Remove(window);
            m_WindowsCache.Remove(window.GetType());
            if (m_CurrentRoot == window)
            {
                m_CurrentRoot = null;
                ActiveTabId = null;
            }
            if (m_PendingRoot == window)
                m_PendingRoot = null;
            CompleteWindowResult(window.GetType(), false, null);
            CompleteQueuedPopup(window);
            window.InternalDestroy();
            OnSortWindowDepth();
            OnSetWindowVisible();
        }

        private static bool CloseWindowInternal(
            UIWindow window,
            bool destroy,
            EWindowCloseReason reason = EWindowCloseReason.Navigation,
            bool force = false)
        {
            if (window == null || window.IsClosing)
                return false;
            if (!force && !window.InternalCanClose(reason))
                return false;

            destroy = destroy || window.CachePolicy == EWindowCachePolicy.DestroyOnClose;
            if (window.IsLoading)
            {
                window.InternalCancelLoad();
                destroy = true;
                FinalizeWindowClose(window, destroy);
            }
            else
            {
                window.InternalClose(reason, destroy, () => FinalizeWindowClose(window, destroy));
                RefreshWindowInteraction();
            }
            return true;
        }

        private static void FinalizeWindowClose(UIWindow window, bool destroy)
        {
            m_OpenStack.Remove(window);
            m_OpenSequence.Remove(window);
            if (m_CurrentRoot == window)
            {
                m_CurrentRoot = null;
                ActiveTabId = null;
            }
            if (m_PendingRoot == window)
                m_PendingRoot = null;

            if (destroy)
            {
                m_WindowsCache.Remove(window.GetType());
                window.InternalDestroy();
            }

            OnSortWindowDepth();
            OnSetWindowVisible();
            WindowClosed?.Invoke(window);
            bool hasResult = m_PendingResults.TryGetValue(window, out object result);
            m_PendingResults.Remove(window);
            CompleteWindowResult(window.GetType(), hasResult, result);
            CompleteQueuedPopup(window);
        }

        private static void TryOpenNextPopup()
        {
            if (m_ActivePopupRequest != null || m_PopupQueue.Count == 0 || !m_IsInitialize)
                return;
            for (int i = 0; i < m_OpenStack.Count; i++)
            {
                if (m_OpenStack[i].WindowLayer == EWindowLayer.PopupLayer)
                    return;
            }

            PopupRequest request = m_PopupQueue.Dequeue();
            m_ActivePopupRequest = request;
            try
            {
                OpenWindowOperation operation = OpenWindowAsync(
                    request.WindowType,
                    window => request.Opened?.Invoke(window),
                    EWindowLayer.PopupLayer,
                    request.UserDatas);
                request.Window = operation.Window;
            }
            catch (Exception exception)
            {
                PLogger.Error($"[Queued Popup Failed] {request.WindowType.FullName}: {exception.Message}");
                m_ActivePopupRequest = null;
                TryOpenNextPopup();
            }
        }

        private static void CompleteQueuedPopup(UIWindow window)
        {
            if (m_ActivePopupRequest != null
                && (m_ActivePopupRequest.Window == window || m_ActivePopupRequest.WindowType == window.GetType()))
                m_ActivePopupRequest = null;
            TryOpenNextPopup();
        }

        internal static bool CloseWindowWithResult<TResult>(UIWindow window, TResult result, bool destroy)
        {
            if (window == null || !m_OpenStack.Contains(window) || window.IsClosing)
                return false;
            if (!m_ResultCallbacks.TryGetValue(window.GetType(), out WindowResultRegistration registration))
            {
                PLogger.Warning($"Window has no result receiver: {window.WindowName}");
                return false;
            }
            if (registration.ResultType != typeof(TResult))
            {
                PLogger.Error($"Window result type mismatch. Expected {registration.ResultType}, actual {typeof(TResult)}.");
                return false;
            }
            if (!window.InternalCanClose(EWindowCloseReason.User))
                return false;

            m_PendingResults[window] = result;
            return CloseWindowInternal(window, destroy, EWindowCloseReason.User, true);
        }

        private static void CompleteWindowResult(Type windowType, bool hasValue, object value)
        {
            if (!m_ResultCallbacks.TryGetValue(windowType, out WindowResultRegistration registration))
                return;
            m_ResultCallbacks.Remove(windowType);
            try
            {
                registration.Callback?.Invoke(hasValue, value);
            }
            catch (Exception exception)
            {
                PLogger.Error($"[Window Result Callback Failed] {windowType.FullName}: {exception.Message}");
            }
        }

        internal static void CancelOpen(UIWindow window)
        {
            if (window == null || !m_OpenStack.Contains(window))
                return;
            CloseWindowInternal(window, true, EWindowCloseReason.User, true);
        }

        private static void RefreshWindowInteraction()
        {
            bool allowInput = true;
            for (int i = 0; i < m_OpenStack.Count; i++)
            {
                if (m_OpenStack[i].IsTransitioning)
                {
                    allowInput = false;
                    break;
                }
            }

            for (int i = 0; i < m_OpenStack.Count; i++)
            {
                UIWindow window = m_OpenStack[i];
                if (window.IsPrepare)
                    window.InternalSetGlobalInputAllowed(allowInput);
            }
        }

        private static void OnSortWindowDepth()
        {
            List<UIWindow> sorted = GetVisualOrder();
            var layerDepth = new Dictionary<EWindowLayer, int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                UIWindow window = sorted[i];
                if (!layerDepth.TryGetValue(window.WindowLayer, out int depth))
                    depth = (int)window.WindowLayer;
                window.Depth = depth;
                layerDepth[window.WindowLayer] = depth + 100;
            }
        }

        private static void OnSetWindowVisible(bool isReload = false)
        {
            List<UIWindow> sorted = GetVisualOrder();
            UIWindow blocker = null;
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                UIWindow candidate = sorted[i];
                if (candidate.IsPrepare && candidate.WindowLayer != EWindowLayer.NavigationLayer && candidate.FullScreen)
                {
                    blocker = candidate;
                    break;
                }
            }

            int blockerIndex = blocker == null ? -1 : sorted.IndexOf(blocker);
            bool hidePersistent = blocker != null && blocker.HidePersistent;
            for (int i = 0; i < sorted.Count; i++)
            {
                UIWindow window = sorted[i];
                if (!window.IsPrepare)
                    continue;
                window.Visible = window.WindowLayer == EWindowLayer.NavigationLayer
                    ? !hidePersistent
                    : blockerIndex < 0 || i >= blockerIndex;
            }
            RefreshWindowInteraction();

#if UNITY_EDITOR
            GetWindowInfos(outinfo);
            PLogger.DebugLog("Window Stack Infos:" + ListToString(outinfo));
#endif
        }
        private static string ListToString(List<WindowInfo> list)
        {
            string str = "";
            for (int i = list.Count - 1; i >= 0; i--)
            {
                str += string.Join(", ",list[i].ToString());

            }
            return str;
        }

        private static List<UIWindow> GetVisualOrder()
        {
            var sorted = new List<UIWindow>(m_OpenStack);
            sorted.Sort((a, b) =>
            {
                int layerCompare = ((int)a.WindowLayer).CompareTo((int)b.WindowLayer);
                if (layerCompare != 0)
                    return layerCompare;
                return GetOpenSequence(a).CompareTo(GetOpenSequence(b));
            });
            return sorted;
        }

        private static long GetOpenSequence(UIWindow window)
        {
            return m_OpenSequence.TryGetValue(window, out long sequence) ? sequence : 0;
        }

        private static UIWindow GetTopWindow(bool includeRoot)
        {
            List<UIWindow> sorted = GetVisualOrder();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                UIWindow window = sorted[i];
                if (!window.Visible || window.WindowLayer == EWindowLayer.NavigationLayer)
                    continue;
                if (!includeRoot && (window == m_CurrentRoot || window.WindowLayer == EWindowLayer.RootLayer))
                    continue;
                return window;
            }
            return null;
        }

        private static UIWindow CreateInstance(Type type)
        {
            UIWindow window = Activator.CreateInstance(type) as UIWindow;
            UIWindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(UIWindowAttribute)) as UIWindowAttribute;
            if (attribute == null)
            {
                attribute = new UIWindowAttribute();
                PLogger.Warning(type.FullName + " no add windowAttribute, use default settings");
            }
            window.Init(
                type.FullName,
                attribute.windowLayer,
                attribute.fullScreen,
                attribute.hidePersistent,
                attribute.cachePolicy);
            return window;
        }
    }
}
