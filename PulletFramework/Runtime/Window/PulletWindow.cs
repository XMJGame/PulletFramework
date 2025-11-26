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
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;

namespace PulletFramework.Window
{
    [Serializable]
    public struct WindowInfo
    {
        public string windowName;
        public int windowLayer;
        public bool visible;
        //public bool isLoadDone;

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
        BackgroudLayer = 1000,  //背景UI，如：主界面---一般情况下用户不能主动关闭，永远处于其它UI的最底层
        NormalLayer = 2000,     //普通UI，一级、二级、三级等窗口---一般由用户点击打开的多级窗口
        PopupLayer = 3000,      //弹窗UI 如：
        TopLayer = 4000,        //顶层UI，如：场景加载
        IndependentLayer = 5000,//独立UI, 只负责创建出来，不负责管理，需要自己管理
    }

    /// <summary>
    /// 界面系统
    /// </summary>
    public static class PulletWindow
    {
        //public static Action<UIWindow> OnOpenCallBack;
        private static bool m_IsInitialize = false;
        //界面层级
        private static Dictionary<EWindowLayer, int> m_WindowLayers = new Dictionary<EWindowLayer, int>();
        //加载过的所有窗口（打开、关闭的）缓存
        private static readonly List<UIWindow> m_WindowsCache = new List<UIWindow>();
        //打开界面栈
        private static readonly List<UIWindow> m_OpenStack = new List<UIWindow>();
        //打开的界面入栈信息 -- 用来 清理界面缓存后，返回后可以加载记录栈数据
        private static readonly Stack<Type> m_WindowsOpenStackInfo = new Stack<Type>();

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
        public static void Initalize(GameObject desktop)
        {
            if (m_IsInitialize)
                throw new Exception($"{nameof(PulletWindow)} is initialized !");

            if (m_IsInitialize == false)
            {
                // 创建驱动器
                m_IsInitialize = true;
                m_GameObject = PulletFrameworks.AddSubsystemGameObject($"[{nameof(PulletWindow)}]");
                if (desktop == null)
                {
                    m_Desktop = new UnityEngine.GameObject("UICanvas");
                    m_Desktop.transform.SetParent(transform);
                    m_Desktop.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                    m_Canvas = m_Desktop.AddComponent<Canvas>();
                    m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    m_CanvasScaler = m_Desktop.AddComponent<CanvasScaler>();
                    m_GraphicRaycaster = m_Desktop.AddComponent<GraphicRaycaster>();
                }
                else
                {
                    m_Desktop = (GameObject)desktop;

                    m_Canvas = m_Desktop.GetComponent<Canvas>();
                    m_CanvasScaler = m_Desktop.GetComponent<CanvasScaler>();
                    m_GraphicRaycaster = m_Desktop.GetComponent<GraphicRaycaster>();

                    if (m_Canvas == null || m_CanvasScaler == null || m_CanvasScaler == null)
                    {
                        PLogger.Error("Canvas、CanvasScaler、GraphicRaycaster 其中有一个或多个组件查找不到");
                    }
                }
                PLogger.Log($"{nameof(PulletWindow)} initalize !");
            }
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
                    window.InternalUpdate();
                }

#if UNITY_ANDROID || UNITY_EDITOR
                //安卓上面监听 home 键
                if (m_HomeKaySwitch && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Home)))
                {
                    //返回当前顶部界面
                    if (m_OpenStack.Count > 0)
                    {
                        UIWindow window = m_OpenStack.Peek();
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
        public static void SetHomeKaySwitch(bool on)
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
		/// 设置屏幕安全区域（异形屏支持）
		/// </summary>
		/// <param name="safeRect">安全区域</param>
		public static void ApplyScreenSafeRect(Rect safeRect)
        {
            UnityEngine.UI.CanvasScaler scaler = m_CanvasScaler;
            if (scaler == null)
            {
                PLogger.Error($"Not found {nameof(UnityEngine.UI.CanvasScaler)} !");
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区坐标系的原点为左下角	
            var rectTrans = m_Desktop.transform as RectTransform;
            rectTrans.offsetMin = new Vector2(posX, posY); //锚框状态下的屏幕左下角偏移向量
            rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); //锚框状态下的屏幕右上角偏移向量
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
               // info.isLoadDone = window.IsLoadDone;
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
            return OpenWindowAsync(typeof(T), null, userDatas);
        }
        public static OpenWindowOperation OpenWindowAsync<T>(Action<UIWindow> openCallBack = null, params System.Object[] userDatas) where T : UIWindow
        {
            return OpenWindowAsync(typeof(T), openCallBack, userDatas);
        }
        private static OpenWindowOperation OpenWindowAsync(Type type, Action<UIWindow> openCallBack = null, params System.Object[] userDatas)
        {
            if (!m_IsInitialize)
            {
                PLogger.Error("PulletWindow no init");
            }
            string windowName = type.FullName;
            // 如果窗口已经存在
            UIWindow window;
            if (IsContainsCache(windowName))
            {
                window = GetCacheWindow(windowName);
                //防止短时间 连续打开同一个界面
                if (!window.IsLoadDone)
                {
                    return null;
                }
                if (window.Visible)
                {
                    return null;
                }
            }
            else
            {
                window = CreateInstance(type);
                //加入缓存列表
                m_WindowsCache.Add(window);
            }
            //压入栈
            //独立的层，自我管理
            if (window.WindowLayer != EWindowLayer.IndependentLayer)
            {
                //弹出窗口
                if (m_OpenStack.Contains(window))
                    m_OpenStack.Pop(window);
                //重新压入
                m_OpenStack.Push(window);
            }
            window.InternalLoad(OnWindowPrepare, userDatas, openCallBack);

            //内部 资源 特殊处理
            if (window.isResources)
                return null;
            var operation = new OpenWindowOperation(window.assetHandle);
            YooAssets.StartOperation(operation);
            return operation;

        }

        /// <summary>
        /// 同步打开窗口
        /// </summary>
        /// <typeparam name="T">窗口类</typeparam>
        /// <param name="userDatas">用户自定义数据</param>
        public static OpenWindowOperation OpenWindowSync<T>(params System.Object[] userDatas) where T : UIWindow
        {
            var operation = OpenWindowAsync(typeof(T), null, userDatas);
            operation.WaitForAsyncComplete();
            return operation;
        }
        public static OpenWindowOperation OpenWindowSync(Type type, params System.Object[] userDatas)
        {
            var operation = OpenWindowAsync(type, null, userDatas);
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
            Type type = typeof(T);
            string windowName = type.FullName;
            UIWindow window = m_OpenStack.GetWindow(windowName);
            if (window == null)
                return false;
            return true;
        }

        /// <summary>
		/// 关闭窗口
		/// </summary>
		public static void CloseWindow<T>(bool destroy = false) where T : UIWindow
        {
            CloseWindow(typeof(T), destroy);
        }
        public static void CloseWindow(Type type, bool destroy = false)
        {
            string windowName = type.FullName;
            UIWindow window = m_OpenStack.GetWindow(windowName);
            if (window == null)
                return;
            //关闭
            window.InternalClose(destroy);
            //从打开栈 移除
            m_OpenStack.Pop(window);
            //刷新界面
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();

            //如果销毁 
            if (destroy)
            {
                //移除缓存
                m_WindowsCache.Remove(window);
                //销毁
                window.InternalDestroy();
            }
        }

        /// <summary>
        /// 关闭所有窗口
        /// </summary>
        public static void CloseAll(bool isRecord = false)
        {
            PLogger.Log("清理所有界面");
            for (int i = 0; i < m_WindowsCache.Count; i++)
            {
                UIWindow window = m_WindowsCache[i];
                //记录 打开信息
                if (isRecord)
                {
                    m_WindowsOpenStackInfo.Push(window.GetType());
                }
                window.InternalDestroy();
            }

            m_WindowsCache.Clear();
            m_OpenStack.Clear();
        }


        /// <summary>
        /// 获取窗口
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static UIWindow GetCacheWindow<T>()
        {
            Type type = typeof(T);
            return GetCacheWindow(type.FullName);
        }


        /// <summary>
        /// 打开退出弹窗
        /// </summary>
        internal static void InternalOpenQuitPopup()
        {
            /// TODO: 增加退出弹窗接口，提供一个默认的GUI的
            OpenWindowAsync(mQuitPopupWindow, mQuitPopupCallBack);
        }
        static List<WindowInfo> outinfo = new List<WindowInfo>();
        private static void OnWindowPrepare(UIWindow window)
        {
            OnSortWindowDepth(window.WindowLayer);
            window.InternalCreate();
            window.InternalOpen();
            OnSetWindowVisible();
        }



        private static void OnSortWindowDepth(EWindowLayer layer)
        {
            int depth = (int)layer;
            for (int i = 0; i < m_OpenStack.Count; i++)
            {
                if (m_OpenStack[i].WindowLayer == layer)
                {
                    m_OpenStack[i].Depth = depth;
                    depth += 100; //注意：每次递增100深度
                }
            }
        }

        private static void OnSetWindowVisible(bool isReload = false)
        {
            bool isHideNext = false;
            for (int i = m_OpenStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = m_OpenStack[i];
                if (isHideNext == false)
                {
                    window.Visible = true;
                    if (window.IsPrepare && window.FullScreen)
                        isHideNext = true;
                }
                else
                {
                    window.Visible = false;
                }
            }

#if UNITY_EDITOR
            GetWindowInfos(outinfo);
            PLogger.Log("Window Stack Infos:" + ListToString(outinfo));
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
            //return string.Join(", ", list);
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
            window.Init(type.FullName, attribute.windowLayer, attribute.fullScreen);
            return window;
        }

        private static UIWindow GetCacheWindow(string name)
        {
            for (int i = 0; i < m_WindowsCache.Count; i++)
            {
                UIWindow window = m_WindowsCache[i];
                if (window.WindowName == name)
                    return window;
            }
            return null;
        }

        private static bool IsContainsCache(string name)
        {
            for (int i = 0; i < m_WindowsCache.Count; i++)
            {
                UIWindow window = m_WindowsCache[i];
                if (window.WindowName == name)
                    return true;
            }
            return false;
        }

        #region open stack 模拟

        private static UIWindow GetWindow(this List<UIWindow> windows, string name)
        {
            for (int i = 0; i < windows.Count; i++)
            {
                UIWindow window = windows[i];
                if (window.WindowName == name)
                    return window;
            }
            return null;
        }

        private static bool IsContains(this List<UIWindow> windows, string name)
        {
            for (int i = 0; i < windows.Count; i++)
            {
                UIWindow window = windows[i];
                if (window.WindowName == name)
                    return true;
            }
            return false;
        }


        private static void Push(this List<UIWindow> windows, UIWindow window)
        {
            // 如果已经存在
            if (windows.IsContains(window.WindowName))
                throw new System.Exception($"Window {window.WindowName} is exist.");

            //// 获取插入到所属层级的位置
            //int insertIndex = -1;
            //for (int i = 0; i < windows.Count; i++)
            //{
            //    if ((int)window.WindowLayer == (int)windows[i].WindowLayer)
            //        insertIndex = i + 1;
            //}

            //// 如果没有所属层级，找到相邻层级
            //if (insertIndex == -1)
            //{
            //    for (int i = 0; i < windows.Count; i++)
            //    {
            //        if ((int)window.WindowLayer > (int)windows[i].WindowLayer)
            //            insertIndex = i + 1;
            //    }
            //}

            //// 如果是空栈或没有找到插入位置
            //if (insertIndex == -1)
            //{
            //    insertIndex = 0;
            //}

            // 最后插入到堆栈
            windows.Add(window);
        }
        private static void Pop(this List<UIWindow> windows, UIWindow window)
        {
            // 从堆栈里移除
            windows.Remove(window);
        }

        private static UIWindow Peek(this List<UIWindow> windows)
        {
            // 从堆栈里移除
            return windows[windows.Count - 1];
        }
        #endregion
    }
}
