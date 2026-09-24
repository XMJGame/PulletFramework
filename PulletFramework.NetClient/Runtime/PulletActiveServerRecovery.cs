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
    internal sealed class PulletActiveServerRecovery : IDisposable
    {
        private readonly Func<bool> _isConnected;
        private readonly Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> _connect;
        private readonly Action _releaseFailedConnection;
        private readonly Func<bool> _canContinue;
        private readonly Action<int> _onAttempt;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;

        private CancellationTokenSource _activeCts;
        private int _running;
        private int _intentVersion;

        public bool IsRunning => Volatile.Read(ref _running) != 0;

        public PulletActiveServerRecovery(
            Func<bool> isConnected,
            Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> connect,
            Action releaseFailedConnection,
            Func<bool> canContinue,
            Action<int> onAttempt,
            Action<string> logInfo,
            Action<string> logWarning)
        {
            _isConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
            _connect = connect ?? throw new ArgumentNullException(nameof(connect));
            _releaseFailedConnection = releaseFailedConnection ?? throw new ArgumentNullException(nameof(releaseFailedConnection));
            _canContinue = canContinue ?? throw new ArgumentNullException(nameof(canContinue));
            _onAttempt = onAttempt ?? throw new ArgumentNullException(nameof(onAttempt));
            _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
            _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        }

        /// <summary>声明用户产生了新连接意图，并取消旧端点恢复。</summary>
        public void Cancel()
        {
            Interlocked.Increment(ref _intentVersion);
            try { Volatile.Read(ref _activeCts)?.Cancel(); }
            catch (ObjectDisposedException) { /* The previous recovery just finished. */ }
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
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                return false;

            int intentVersion = Volatile.Read(ref _intentVersion);
            var owner = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            _activeCts = owner;
            _logWarning($"快速重连已耗尽，将持续恢复最后服务器 {target}。原因：{reason}");
            _ = RunAsync(target, Math.Max(0.5f, retryDelaySeconds), intentVersion, owner);
            return true;
        }

        private async Task RunAsync(
            PulletServerInfo target,
            float retryDelaySeconds,
            int intentVersion,
            CancellationTokenSource owner)
        {
            int attempt = 0;
            try
            {
                while (!owner.IsCancellationRequested && _canContinue() && !_isConnected() &&
                       intentVersion == Volatile.Read(ref _intentVersion))
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), owner.Token);
                    if (owner.IsCancellationRequested || _isConnected() || !_canContinue() ||
                        intentVersion != Volatile.Read(ref _intentVersion))
                        return;

                    attempt++;
                    _onAttempt(attempt);
                    _logInfo($"正在第 {attempt} 次持续恢复服务器：{target}");
                    ConnectionResult result = await _connect(target, owner.Token);
                    if (owner.IsCancellationRequested ||
                        intentVersion != Volatile.Read(ref _intentVersion)) return;
                    if (result.IsSuccess)
                    {
                        _logInfo($"已恢复最后服务器：{target}");
                        return;
                    }

                    _logWarning($"持续恢复失败：{FormatError(result)}");
                    _releaseFailedConnection();
                }
            }
            catch (OperationCanceledException) when (owner.IsCancellationRequested)
            {
                // 新连接意图、主动断开和应用退出均属于正常取消。
            }
            catch (Exception exception)
            {
                _logWarning("持续恢复服务器异常：" + exception.Message);
            }
            finally
            {
                Interlocked.CompareExchange(ref _activeCts, null, owner);
                owner.Dispose();
                Interlocked.Exchange(ref _running, 0);
            }
        }

        public void Dispose() => Cancel();

        private static string FormatError(ConnectionResult result)
            => string.IsNullOrWhiteSpace(result.Message) ? result.ErrorCode.ToString() : result.Message;
    }
}
