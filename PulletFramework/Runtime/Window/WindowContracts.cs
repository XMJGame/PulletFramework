using System;

namespace PulletFramework.Window
{
    /// <summary>
    /// 窗口关闭后的资源保留策略。
    /// </summary>
    public enum EWindowCachePolicy
    {
        Cache,
        DestroyOnClose,
        Permanent,
    }

    /// <summary>
    /// 窗口关闭的来源，可用于未保存内容、战斗退出等拦截。
    /// </summary>
    public enum EWindowCloseReason
    {
        User,
        Back,
        Navigation,
        CloseAll,
    }

    /// <summary>
    /// 窗口进出场过渡。除主动 Cancel 外，完成回调必须且只能调用一次。
    /// </summary>
    public interface IUIWindowTransition
    {
        bool IsPlaying { get; }
        void PlayEnter(Action completed);
        void PlayExit(Action completed);
        void CompleteImmediately();
        void Cancel();
    }

    /// <summary>
    /// 窗口结果回调参数。HasValue 为 false 表示窗口未提交结果便被关闭。
    /// </summary>
    public readonly struct UIWindowResult<TResult>
    {
        public bool HasValue { get; }
        public TResult Value { get; }

        internal UIWindowResult(bool hasValue, TResult value)
        {
            HasValue = hasValue;
            Value = value;
        }
    }
}
