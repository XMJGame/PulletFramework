using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulletFramework.MiniGame.Platform
{
    /// <summary>供平台桥接实现复用的轻量 Task 辅助方法。</summary>
    public static class PlatformTask
    {
        public static Task<T> FromResult<T>(T result, CancellationToken cancellationToken = default)
        {
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<T>(cancellationToken)
                : Task.FromResult(result);
        }

        // 取消某个等待者不会终止由其他调用者共享的平台请求。
        public static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
                return await task;

            var cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (task != await Task.WhenAny(task, cancelled.Task))
                    throw new OperationCanceledException(cancellationToken);
            }

            return await task;
        }
    }
}
