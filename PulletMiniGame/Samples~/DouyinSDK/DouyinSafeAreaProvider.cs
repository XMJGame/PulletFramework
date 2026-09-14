#if UNITY_WEBGL && (PULLET_PLATFORM_DOUYIN || DOUYINMINIGAME)
using PulletFramework.Window;
using TTSDK;
using UnityEngine;

namespace PulletMiniGame.Platform.Douyin
{
    /// <summary>
    /// 使用抖音 SDK 安全区。平台坐标为左上角原点的逻辑像素。
    /// </summary>
    internal sealed class DouyinSafeAreaProvider : IUISafeAreaProvider
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
                TTSystemInfo info = TT.GetSystemInfo();
                SafeArea? safeArea = info?.safeArea;
                if (!safeArea.HasValue)
                {
                    result = default;
                    return false;
                }

                SafeArea area = safeArea.Value;
                result = UISafeArea.FromTopLeftLogical(
                    area.left,
                    area.top,
                    area.right,
                    area.bottom,
                    info.screenWidth,
                    info.screenHeight);
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
