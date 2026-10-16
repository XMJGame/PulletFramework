using System;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    /// <summary>
    /// 在底层有限次数快速重连耗尽后，按固定间隔恢复最后一个明确端点。
    /// 新连接意图会取消旧恢复，保证不会重新连回用户已经放弃的服务器。
    /// </summary>
    internal sealed class PulletActiveServerRecovery : IDisposable, IAsyncDisposable
    {
        private readonly Func<bool> _isConnected;
        private readonly Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> _connect;
        private readonly Action _releaseFailedConnection;
        private readonly Func<bool> _canContinue;
        private readonly Action<int> _onAttempt;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly object _gate = new object();
        private RunOwner _active;
        private Task _completion = Task.CompletedTask;
        private Task _stop;
        private bool _closed;
        private Exception _executionFailure;
        private long _intentVersion;

        public bool IsRunning { get { lock (_gate) return _active != null; } }
        // The most recently accepted run, including its failure and real exit.
        // Cancel requests cancellation; it never claims that this task completed.
        internal Task Completion { get { lock (_gate) return _completion; } }

        public PulletActiveServerRecovery(
            Func<bool> isConnected,
            Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> connect,
            Action releaseFailedConnection,
            Func<bool> canContinue,
            Action<int> onAttempt,
            Action<string> logInfo,
            Action<string> logWarning,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _isConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
            _connect = connect ?? throw new ArgumentNullException(nameof(connect));
            _releaseFailedConnection = releaseFailedConnection ?? throw new ArgumentNullException(nameof(releaseFailedConnection));
            _canContinue = canContinue ?? throw new ArgumentNullException(nameof(canContinue));
            _onAttempt = onAttempt ?? throw new ArgumentNullException(nameof(onAttempt));
            _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
            _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
            _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        }

        /// <summary>声明用户产生了新连接意图，并取消旧端点恢复。</summary>
        public void Cancel()
        {
            RunOwner owner;
            lock (_gate) { ++_intentVersion; owner = _active; }
            RequestCancellation(owner);
        }

        /// <summary>启动持续恢复；已有恢复运行、目标无效或生命周期结束时返回 false。</summary>
        public bool Start(
            PulletServerInfo target,
            float retryDelaySeconds,
            string reason,
            CancellationToken lifetimeToken)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.host) || lifetimeToken.IsCancellationRequested)
                return false;
            RunOwner owner;
            lock (_gate)
            {
                if (_closed || _active != null || _executionFailure != null) return false;
                owner = new RunOwner(_intentVersion, CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken));
                _active = owner; _completion = owner.Exited.Task;
            }
            // Publish the owner before any diagnostic or delegate can reenter.
            // The original caller scheduler is preserved: this strategy does not
            // silently move application delegates onto a new background worker.
            _ = RunAsync(target, Math.Max(0.5f, retryDelaySeconds), reason, owner);
            return true;
        }

        private async Task RunAsync(
            PulletServerInfo target,
            float retryDelaySeconds,
            string reason,
            RunOwner owner)
        {
            int attempt = 0;
            Exception failure = null;
            try
            {
                owner.Cancellation.Token.ThrowIfCancellationRequested();
                _logWarning($"快速重连已耗尽，将持续恢复最后服务器 {target}。原因：{reason}");
                while (IsCurrent(owner) && _canContinue() && !_isConnected())
                {
                    await _delay(TimeSpan.FromSeconds(retryDelaySeconds), owner.Cancellation.Token);
                    if (!IsCurrent(owner) || _isConnected() || !_canContinue())
                        return;

                    attempt++;
                    _onAttempt(attempt);
                    if (!IsCurrent(owner)) return;
                    _logInfo($"正在第 {attempt} 次持续恢复服务器：{target}");
                    // Application callbacks may request Stop/Cancel reentrantly.
                    if (!IsCurrent(owner)) return;
                    ConnectionResult result = await _connect(target, owner.Cancellation.Token);
                    if (!IsCurrent(owner)) return;
                    if (result.IsSuccess)
                    {
                        _logInfo($"已恢复最后服务器：{target}");
                        return;
                    }

                    _logWarning($"持续恢复失败：{FormatError(result)}");
                    _releaseFailedConnection();
                }
            }
            catch (OperationCanceledException error) when (owner.Cancellation.IsCancellationRequested
                && error.CancellationToken == owner.Cancellation.Token)
            {
                // 新连接意图、主动断开和应用退出均属于正常取消。
            }
            catch (Exception exception)
            {
                failure = exception;
                try { _logWarning("持续恢复服务器异常：" + exception.Message); }
                catch (Exception diagnostic) { failure = new AggregateException(exception, diagnostic); }
            }
            finally
            {
                owner.Cancellation.Dispose();
                lock (_gate)
                {
                    _active = null;
                    if (failure == null) owner.Exited.TrySetResult(true);
                    else { _executionFailure = failure; owner.Exited.TrySetException(failure); }
                }
            }
        }

        private bool IsCurrent(RunOwner owner)
        { lock (_gate) return !_closed && ReferenceEquals(_active, owner) && owner.Version == _intentVersion && !owner.Cancellation.IsCancellationRequested; }

        // Permanent close. A canceled waiter cannot cancel the shared join;
        // non-cooperative Connect work remains owned until its real completion.
        internal Task StopAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled(ct);
            RunOwner run; Task work; Task shared; TaskCompletionSource<bool> owner = null;
            lock (_gate)
            {
                if (_stop == null)
                {
                    _closed = true; ++_intentVersion;
                    owner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _stop = owner.Task;
                }
                shared = _stop; run = _active; work = _completion;
            }
            if (owner != null) _ = CompleteStopAsync(run, work, owner);
            return ct.CanBeCanceled ? WaitForCallerAsync(shared, ct) : shared;
        }

        private static async Task CompleteStopAsync(RunOwner run, Task work, TaskCompletionSource<bool> owner)
        {
            Exception failure = null;
            try { RequestCancellation(run); } catch (Exception error) { failure = error; }
            try { await work.ConfigureAwait(false); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            if (failure == null) owner.TrySetResult(true); else owner.TrySetException(failure);
        }

        private static void RequestCancellation(RunOwner owner)
        {
            try { owner?.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { /* The captured execution already exited. */ }
        }

        private static async Task WaitForCallerAsync(Task work, CancellationToken ct)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(work, canceled.Task).ConfigureAwait(false) != work) ct.ThrowIfCancellationRequested();
                await work.ConfigureAwait(false);
            }
        }

        // Synchronous disposal requests permanent close; only the stable Task or
        // DisposeAsync proves completion. Never block an application callback here.
        public void Dispose() { _ = StopAsync(); }
        public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); }

        private sealed class RunOwner
        {
            internal readonly long Version;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly TaskCompletionSource<bool> Exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal RunOwner(long version, CancellationTokenSource cancellation) { Version = version; Cancellation = cancellation; }
        }

        private static string FormatError(ConnectionResult result)
            => string.IsNullOrWhiteSpace(result.Message) ? result.ErrorCode.ToString() : result.Message;
    }
}
