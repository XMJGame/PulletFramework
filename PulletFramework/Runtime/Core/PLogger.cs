using System;

namespace PulletFramework
{
    public enum EPulletLogLevel
    {
        Off = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
        Debug = 4
    }

    /// <summary>Pullet 各模块统一日志入口。</summary>
    public static class PLogger
    {
        private const string Prefix = "[PulletFramework] ";
        public static EPulletLogLevel Level { get; set; } = EPulletLogLevel.Info;

        public static void Log(string info)
        {
            Info(info);
        }

        public static void Info(string info)
        {
            if (Level < EPulletLogLevel.Info)
                return;
            UnityEngine.Debug.Log(Prefix + info);
        }

        public static void DebugLog(string info)
        {
            if (Level < EPulletLogLevel.Debug)
                return;
            UnityEngine.Debug.Log(Prefix + info);
        }

        public static void Warning(string info)
        {
            if (Level < EPulletLogLevel.Warning)
                return;
            UnityEngine.Debug.LogWarning(Prefix + info);
        }

        public static void Error(string info)
        {
            if (Level < EPulletLogLevel.Error)
                return;
            UnityEngine.Debug.LogError(Prefix + info);
        }

        public static void Exception(Exception exception, string context = null)
        {
            if (exception == null || Level < EPulletLogLevel.Error)
                return;
            string message = string.IsNullOrWhiteSpace(context)
                ? exception.ToString()
                : context + "\n" + exception;
            UnityEngine.Debug.LogError(Prefix + message);
        }

#if UNITY_EDITOR
        public static void EditorInfo(string info)
        {
            UnityEngine.Debug.Log(Prefix + info);
        }

        public static void EditorWarning(string info)
        {
            UnityEngine.Debug.LogWarning(Prefix + info);
        }

        public static void EditorError(string info)
        {
            UnityEngine.Debug.LogError(Prefix + info);
        }

        public static void EditorException(Exception exception, string context = null)
        {
            if (exception == null)
                return;
            string message = string.IsNullOrWhiteSpace(context)
                ? exception.ToString()
                : context + "\n" + exception;
            UnityEngine.Debug.LogError(Prefix + message);
        }
#endif
    }
}
