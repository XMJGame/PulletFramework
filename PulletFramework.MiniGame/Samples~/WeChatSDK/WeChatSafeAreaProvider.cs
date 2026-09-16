#if UNITY_WEBGL && (WEIXINMINIGAME || UNITY_WECHATMINIGAME)
using PulletFramework.Window;
using UnityEngine;
using WeChatWASM;

namespace PulletFramework.MiniGame.Platform.WeChat
{
    /// <summary>
    /// 使用微信小游戏窗口安全区。平台坐标为左上角原点的逻辑像素。
    /// </summary>
    internal sealed class WeChatSafeAreaProvider : IUISafeAreaProvider
    {
        private Rect m_CachedArea;
        private int m_ScreenWidth = -1;
        private int m_ScreenHeight = -1;
        private bool m_HasPlatformArea;
        private float m_NextRetryTime;

        public Rect GetSafeArea()
        {
            bool screenChanged = m_ScreenWidth != Screen.width || m_ScreenHeight != Screen.height;
            if (!screenChanged && (m_HasPlatformArea || Time.realtimeSinceStartup < m_NextRetryTime))
                return m_CachedArea;

            m_ScreenWidth = Screen.width;
            m_ScreenHeight = Screen.height;
            m_HasPlatformArea = TryReadSafeArea(out Rect safeArea);
            m_CachedArea = m_HasPlatformArea ? safeArea : Screen.safeArea;
            m_NextRetryTime = Time.realtimeSinceStartup + 1f;
            return m_CachedArea;
        }

        private static bool TryReadSafeArea(out Rect result)
        {
            try
            {
                WindowInfo info = WX.GetWindowInfo();
                SafeArea area = info?.safeArea;
                if (area == null)
                {
                    result = default;
                    return false;
                }

                float width = info.windowWidth > 0f ? info.windowWidth : info.screenWidth;
                float height = info.windowHeight > 0f ? info.windowHeight : info.screenHeight;
                result = UISafeArea.FromTopLeftLogical(
                    area.left,
                    area.top,
                    area.right,
                    area.bottom,
                    width,
                    height);
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }
    }
}
#endif
