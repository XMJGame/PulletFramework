using PulletNet.Messaging;

namespace PulletFramework.Messaging
{
    public enum MessageInputKind { Response, Notification, SourceMismatch, Closed, InvalidFrame, UnexpectedKind }

    /// <summary>Receive admission/dispatch facts, not remote delivery or business processing acknowledgement.</summary>
    public readonly struct MessageInputResult
    {
        internal MessageInputResult(MessageInputKind kind, FrameError error = FrameError.None,
            ResponseResult? response = null, NotificationDispatchReport? notification = null)
        { Kind = kind; FrameError = error; Response = response; Notification = notification; }
        public MessageInputKind Kind { get; }
        public FrameError FrameError { get; }
        public ResponseResult? Response { get; }
        public NotificationDispatchReport? Notification { get; }
    }
}
