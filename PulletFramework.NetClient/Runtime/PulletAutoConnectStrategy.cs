using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    /// <summary>
    /// 纯自动连接决策器。它不持有 Socket 或 Unity 生命周期，只决定何时连接固定端点、
    /// 何时发现服务器，以及发现失败后是否进入下一轮。
    /// </summary>
    internal sealed class PulletAutoConnectStrategy
    {
        internal sealed class Options
        {
            public AutoConnectMode Mode;
            public bool EnableDiscovery;
            public bool RetryDiscoveryUntilConnected;
            public float DiscoveryRetrySeconds;
            public string FixedEndpointDescription;
            public string DiscoveryDescription;
        }

        private readonly Func<CancellationToken, Task<ConnectionResult>> _connectFixed;
        private readonly Func<CancellationToken, Task<IReadOnlyList<PulletServerInfo>>> _discover;
        private readonly Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> _connectDiscovered;
        private readonly Action _releaseFailedConnection;
        private readonly Func<bool> _canContinue;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;

        public PulletAutoConnectStrategy(
            Func<CancellationToken, Task<ConnectionResult>> connectFixed,
            Func<CancellationToken, Task<IReadOnlyList<PulletServerInfo>>> discover,
            Func<PulletServerInfo, CancellationToken, Task<ConnectionResult>> connectDiscovered,
            Action releaseFailedConnection,
            Func<bool> canContinue,
            Action<string> logInfo,
            Action<string> logWarning)
        {
            _connectFixed = connectFixed ?? throw new ArgumentNullException(nameof(connectFixed));
            _discover = discover ?? throw new ArgumentNullException(nameof(discover));
            _connectDiscovered = connectDiscovered ?? throw new ArgumentNullException(nameof(connectDiscovered));
            _releaseFailedConnection = releaseFailedConnection ?? throw new ArgumentNullException(nameof(releaseFailedConnection));
            _canContinue = canContinue ?? throw new ArgumentNullException(nameof(canContinue));
            _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
            _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        }

        /// <summary>执行一次完整自动连接策略，直到成功、取消或策略允许的尝试耗尽。</summary>
        public async Task<ConnectionResult> RunAsync(Options options, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _logInfo($"自动连接开始：Mode={options.Mode}。");

            if (!options.EnableDiscovery)
            {
                _logWarning($"局域网发现已禁用，改为连接固定服务器：{options.FixedEndpointDescription}");
                ConnectionResult fixedResult = await _connectFixed(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                LogResult(fixedResult, "固定服务器");
                return fixedResult;
            }

            if (options.Mode != AutoConnectMode.DiscoverFirst)
            {
                _logInfo($"{options.Mode}：正在尝试固定服务器 {options.FixedEndpointDescription}。");
                ConnectionResult direct = await _connectFixed(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (direct.IsSuccess)
                {
                    _logInfo($"{options.Mode}：固定服务器连接成功。");
                    return direct;
                }
                if (options.Mode == AutoConnectMode.Direct)
                {
                    _logWarning($"Direct：固定服务器连接失败，不会启动发现。原因：{FormatError(direct)}");
                    return direct;
                }

                _logWarning($"DirectThenDiscover：固定服务器连接失败，切换到局域网发现。原因：{FormatError(direct)}");
                _releaseFailedConnection();
            }
            else
            {
                _logInfo("DiscoverFirst：跳过固定地址，优先搜索局域网内可用服务器。");
            }

            ConnectionResult last = ConnectionResult.Fail(
                ConnectionErrorCode.TransportUnavailable,
                "No matching server was discovered.");
            int discoveryRound = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                discoveryRound++;
                _logInfo($"开始第 {discoveryRound} 轮服务器发现：{options.DiscoveryDescription}。");
                IReadOnlyList<PulletServerInfo> servers = await _discover(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                PulletServerInfo selected = SelectPreferredServer(servers);
                if (selected != null)
                {
                    _logInfo($"发现 {servers.Count} 个匹配服务器，自动选择：{selected}");
                    last = await _connectDiscovered(selected, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (last.IsSuccess)
                    {
                        _logInfo($"自动连接成功：{selected}");
                        return last;
                    }
                    _logWarning($"已发现服务器但连接失败：{selected}；原因：{FormatError(last)}");
                    _releaseFailedConnection();
                }
                else
                {
                    _logWarning($"第 {discoveryRound} 轮未发现匹配服务器。");
                }

                if (!options.RetryDiscoveryUntilConnected)
                {
                    _logWarning("自动连接结束：未启用发现循环重试。");
                    return last;
                }

                double delaySeconds = Math.Max(0.25f, options.DiscoveryRetrySeconds);
                _logInfo($"将在 {delaySeconds:0.##} 秒后重新发现服务器。");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            while (!cancellationToken.IsCancellationRequested && _canContinue());

            return last;
        }

        /// <summary>使用稳定名称和实例 ID 保证自动选择结果可复现。</summary>
        internal static PulletServerInfo SelectPreferredServer(IReadOnlyList<PulletServerInfo> servers)
        {
            if (servers == null || servers.Count == 0) return null;
            return servers
                .OrderBy(server => server.serverName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(server => server.instanceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private void LogResult(ConnectionResult result, string target)
        {
            if (result.IsSuccess) _logInfo($"自动连接成功：{target}。");
            else _logWarning($"自动连接失败：{target}；原因：{FormatError(result)}");
        }

        private static string FormatError(ConnectionResult result)
            => string.IsNullOrWhiteSpace(result.Message) ? result.ErrorCode.ToString() : result.Message;
    }
}
