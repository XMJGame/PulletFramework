using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;
using PulletClient = PulletNet.ClientSDK.NetClient;
using UnityEngine;
using UnityEngine.Events;

namespace PulletFramework.NetClient
{
    public enum AutoConnectMode : byte
    {
        Direct = 0,
        DiscoverFirst = 1,
        DirectThenDiscover = 2
    }

    /// <summary>Unity 生命周期、Inspector 配置、局域网发现和 PulletNet ClientSDK 的统一入口。</summary>
    public sealed class PulletNetworkManager : MonoBehaviour
    {
        [Serializable] public sealed class BoolEvent : UnityEvent<bool> { }
        [Serializable] public sealed class StringEvent : UnityEvent<string> { }
        [Serializable] public sealed class BytesEvent : UnityEvent<byte[]> { }
        [Serializable] public sealed class ServerListEvent : UnityEvent<PulletServerInfo[]> { }
        [Serializable] public sealed class DiscoveryAttemptEvent : UnityEvent<int, int> { }

        public static PulletNetworkManager Instance { get; private set; }

        [Header("服务器")]
        public string host = "127.0.0.1";
        public int tcpPort = 7778;
        public int udpPort = 7777;
        public int webSocketPort = 7779;
        public string webSocketPath = "/ws";
        public bool webSocketUseTls;

        [Header("连接")]
        public ConnectionMode connectionMode = ConnectionMode.UdpWithTcpControl;
        public AutoConnectMode autoConnectMode = AutoConnectMode.DirectThenDiscover;
        public bool connectOnStart = true;
        public bool dontDestroyOnLoad = true;
        public bool reconnectOnApplicationResume = true;
        public bool enableKeepAlive = true;
        public bool enableReconnect = true;
        [Min(0)] public int maxReconnectAttempts = 10;

        [Header("局域网发现")]
        public bool enableDiscovery = true;
        public int discoveryPort = 58888;
        [Min(0.25f)] public float discoveryTimeoutSeconds = 3f;
        [Min(1)] public int discoveryMaxAttempts = 5;
        [Min(0.1f)] public float discoverySendIntervalSeconds = 0.5f;
        [Min(0.25f)] public float discoveryRetrySeconds = 1f;
        public bool retryDiscoveryUntilConnected = true;
        public string discoveryServiceType = "pulletnet";

        [Header("主线程事件队列")]
        [Min(1)] public int payloadQueueCapacity = 1024;
        [Min(1)] public int maxPayloadCallbacksPerFrame = 256;

        [Header("客户端标识")]
        public string clientVersion = "1.0.0";
        [Range(0, 255)] public int runtimeId = 1;

        [Header("日志")]
        [Tooltip("输出连接模式、服务器发现、连接结果及重连状态等关键日志。不会输出每个数据包。")]
        public bool enableConnectionLogs = true;

        [Header("连接事件（Inspector 与代码均可订阅）")]
        public BoolEvent OnConnected = new BoolEvent();
        public StringEvent OnConnectedFailed = new StringEvent();
        public UnityEvent OnDisconnected = new UnityEvent();
        public BytesEvent OnMessageReceived = new BytesEvent();
        public StringEvent OnNetworkError = new StringEvent();

        [Header("发现事件（Inspector 与代码均可订阅）")]
        public UnityEvent OnDiscoveryStarted = new UnityEvent();
        public DiscoveryAttemptEvent OnDiscoveryAttempt = new DiscoveryAttemptEvent();
        public ServerListEvent OnDiscoverySucceeded = new ServerListEvent();
        public StringEvent OnDiscoveryFailed = new StringEvent();
        public UnityEvent OnDiscoveryStopped = new UnityEvent();

        private sealed class MainThreadWorkItem
        {
            private readonly Action _action;
            public MainThreadWorkItem(Action action) => _action = action;
            public void Complete(bool invoke) { if (invoke) _action(); }
        }

        private readonly ConcurrentQueue<MainThreadWorkItem> _mainThreadActions = new ConcurrentQueue<MainThreadWorkItem>();
        private readonly object _clientGate = new object();
        private PulletClient _client;
        private BoundedPayloadQueue _payloadQueue;
        private CancellationTokenSource _lifetimeCts;
        private bool _resumeShouldReconnect;
        private int _pendingReliablePayloadOverflows;
        private volatile bool _isShuttingDown;
        private int _autoConnectRunning;
        private string _activeHost;
        private int _activeTcpPort;
        private int _activeUdpPort;
        private int _activeWebSocketPort;

        public bool IsConnected => _client != null && _client.State == ConnectionState.Connected;
        public bool IsDiscovering { get; private set; }
        public PulletServerInfo ActiveServer { get; private set; }
        public IReadOnlyList<PulletServerInfo> LastDiscoveredServers { get; private set; } = Array.Empty<PulletServerInfo>();
        public long DroppedPayloadCount => _payloadQueue != null ? _payloadQueue.DroppedCount : 0;
        public long ReliablePayloadOverflowCount => _payloadQueue != null ? _payloadQueue.ReliableOverflowCount : 0;

        public event Action<ConnectedEvent> Connected;
        public event Action<ConnectFailedEvent> ConnectFailed;
        public event Action<DisconnectedEvent> Disconnected;
        public event Action<ReceivedPayload> PayloadReceived;
        public event Action<string> PayloadQueueFaulted;
        public event Action<ReconnectAttemptEvent> ReconnectAttempt;
        public event Action Reconnected;
        public event Action<ReconnectFailedEvent> ReconnectFailed;
        public event Action DiscoveryStarted;
        public event Action<int, int> DiscoveryAttempted;
        public event Action<IReadOnlyList<PulletServerInfo>> DiscoverySucceeded;
        public event Action<string> DiscoveryFailed;
        public event Action DiscoveryStopped;
        public event Action<IReadOnlyList<PulletServerInfo>> ServersDiscovered;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
            _lifetimeCts = new CancellationTokenSource();
            ResetPayloadQueue();
            BindDiscoveryEvents();
        }

        private async void Start()
        {
            if (connectOnStart)
                await AutoConnectAndReportAsync();
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var workItem))
            {
                try { workItem.Complete(!_isShuttingDown); }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            DrainPayloadQueue();
            int reliableOverflows = Interlocked.Exchange(ref _pendingReliablePayloadOverflows, 0);
            if (reliableOverflows <= 0) return;

            string error = $"Reliable payload queue overflowed {reliableOverflows} time(s); application delivery is no longer complete.";
            Debug.LogError($"[PulletNet] {error}");
            try
            {
                PayloadQueueFaulted?.Invoke(error);
                OnNetworkError?.Invoke(error);
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        public async Task<ConnectionResult> ConnectAsync()
        {
            LogInfo($"手动连接固定服务器：{FormatEndpoint(host, tcpPort, udpPort)}");
            ConnectionResult result = await ConnectEndpointAsync(host, tcpPort, udpPort, webSocketPort, null);
            if (!result.IsSuccess) Enqueue(() => ReportConnectFailure(result));
            return result;
        }

        public async Task<ConnectionResult> ConnectAsync(string targetHost)
        {
            LogInfo($"手动连接指定服务器：{FormatEndpoint(targetHost, tcpPort, udpPort)}");
            ConnectionResult result = await ConnectEndpointAsync(targetHost, tcpPort, udpPort, webSocketPort, null);
            if (!result.IsSuccess) Enqueue(() => ReportConnectFailure(result));
            return result;
        }

        public async Task<ConnectionResult> ConnectAsync(PulletServerInfo server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            LogInfo($"连接已选择的服务器：{FormatServer(server)}");
            ConnectionResult result = await ConnectEndpointAsync(
                server.host,
                server.tcpPort > 0 ? server.tcpPort : tcpPort,
                server.udpPort > 0 ? server.udpPort : udpPort,
                server.webPort > 0 ? server.webPort : webSocketPort,
                server);
            if (!result.IsSuccess) Enqueue(() => ReportConnectFailure(result));
            return result;
        }

        public async Task<ConnectionResult> AutoConnectAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _autoConnectRunning, 1) != 0)
            {
                LogWarning("自动连接请求被忽略：已有自动连接流程正在运行。");
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "Auto connect is already running.");
            }

            try
            {
                LogInfo($"自动连接开始：Mode={autoConnectMode}（{DescribeAutoConnectMode(autoConnectMode)}）。");
                if (!enableDiscovery)
                {
                    LogWarning($"局域网发现已禁用，改为连接固定服务器：{FormatEndpoint(host, tcpPort, udpPort)}");
                    ConnectionResult fixedResult = await ConnectEndpointAsync(host, tcpPort, udpPort, webSocketPort, null);
                    LogAutoConnectResult(fixedResult, "固定服务器");
                    return fixedResult;
                }

                if (autoConnectMode != AutoConnectMode.DiscoverFirst)
                {
                    LogInfo($"{autoConnectMode}：正在尝试固定服务器 {FormatEndpoint(host, tcpPort, udpPort)}。");
                    ConnectionResult direct = await ConnectEndpointAsync(host, tcpPort, udpPort, webSocketPort, null);
                    if (direct.IsSuccess)
                    {
                        LogInfo($"{autoConnectMode}：固定服务器连接成功。");
                        return direct;
                    }
                    if (autoConnectMode == AutoConnectMode.Direct)
                    {
                        LogWarning($"Direct：固定服务器连接失败，不会启动发现。原因：{FormatResultError(direct)}");
                        return direct;
                    }

                    LogWarning($"DirectThenDiscover：固定服务器连接失败，切换到局域网发现。原因：{FormatResultError(direct)}");
                    ReleaseDisconnectedClient();
                }
                else
                {
                    LogInfo("DiscoverFirst：跳过固定地址，优先搜索局域网内可用服务器。");
                }

                ConnectionResult last = ConnectionResult.Fail(ConnectionErrorCode.TransportUnavailable, "No matching server was discovered.");
                int discoveryRound = 0;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    discoveryRound++;
                    LogInfo($"开始第 {discoveryRound} 轮服务器发现：UDP {discoveryPort}，ServiceType={discoveryServiceType}。");
                    IReadOnlyList<PulletServerInfo> servers = await DiscoverAsync(cancellationToken);
                    PulletServerInfo selected = SelectPreferredServer(servers);
                    if (selected != null)
                    {
                        LogInfo($"发现 {servers.Count} 个匹配服务器，自动选择：{FormatServer(selected)}");
                        last = await ConnectEndpointAsync(
                            selected.host,
                            selected.tcpPort > 0 ? selected.tcpPort : tcpPort,
                            selected.udpPort > 0 ? selected.udpPort : udpPort,
                            selected.webPort > 0 ? selected.webPort : webSocketPort,
                            selected);
                        if (last.IsSuccess)
                        {
                            LogInfo($"自动连接成功：{FormatServer(selected)}");
                            return last;
                        }
                        LogWarning($"已发现服务器但连接失败：{FormatServer(selected)}；原因：{FormatResultError(last)}");
                        ReleaseDisconnectedClient();
                    }
                    else
                    {
                        LogWarning($"第 {discoveryRound} 轮未发现匹配服务器。");
                    }

                    if (!retryDiscoveryUntilConnected)
                    {
                        LogWarning("自动连接结束：未启用发现循环重试。");
                        return last;
                    }
                    LogInfo($"将在 {Math.Max(0.25f, discoveryRetrySeconds):0.##} 秒后重新发现服务器。");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.25f, discoveryRetrySeconds)), cancellationToken);
                }
                while (!cancellationToken.IsCancellationRequested && !_isShuttingDown);

                return last;
            }
            catch (OperationCanceledException)
            {
                LogWarning("自动连接已取消。");
                return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Auto connect was cancelled.");
            }
            finally
            {
                Interlocked.Exchange(ref _autoConnectRunning, 0);
            }
        }

        public async Task<IReadOnlyList<PulletServerInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            if (!enableDiscovery)
                return Array.Empty<PulletServerInfo>();
            if (!IsValidPort(discoveryPort))
                throw new InvalidOperationException("Discovery port must be between 1 and 65535.");

            IsDiscovering = true;
            try
            {
                PublishDiscoveryStarted();
                IReadOnlyList<PulletServerInfo> servers = await PulletServerDiscovery.DiscoverAsync(
                    discoveryPort,
                    discoveryTimeoutSeconds,
                    discoveryMaxAttempts,
                    discoverySendIntervalSeconds,
                    discoveryServiceType,
                    (attempt, maximum) => Enqueue(() => PublishDiscoveryAttempt(attempt, maximum)),
                    cancellationToken);
                LastDiscoveredServers = servers;
                if (servers.Count > 0)
                    Enqueue(() => PublishDiscoverySuccess(servers));
                else
                    Enqueue(() => PublishDiscoveryFailure(
                        $"未发现服务类型为 {discoveryServiceType} 的服务器。"));
                return servers;
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<PulletServerInfo>();
            }
            catch (Exception ex)
            {
                Enqueue(() => PublishDiscoveryFailure(ex.Message));
                return Array.Empty<PulletServerInfo>();
            }
            finally
            {
                IsDiscovering = false;
                Enqueue(PublishDiscoveryStopped);
            }
        }

        /// <summary>开始一次可由 StopDiscovery 停止的有限次数发现。</summary>
        public bool StartDiscovery()
        {
            if (!enableDiscovery)
            {
                PublishDiscoveryFailure("服务器发现已禁用。");
                return false;
            }

            try
            {
                bool started = PulletServerDiscovery.StartDiscovery(
                    discoveryPort,
                    discoveryTimeoutSeconds,
                    discoveryMaxAttempts,
                    discoverySendIntervalSeconds,
                    discoveryServiceType);
                if (started) IsDiscovering = true;
                else LogWarning("服务器发现请求被忽略：已有发现流程正在运行。");
                return started;
            }
            catch (Exception ex)
            {
                PublishDiscoveryFailure(ex.Message);
                return false;
            }
        }

        public void StopDiscovery() => PulletServerDiscovery.StopDiscovery();

        public async Task<ConnectionResult> DisconnectAsync()
        {
            try
            {
                PulletClient client = _client;
                return client == null ? ConnectionResult.Ok() : await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message);
            }
        }

        public async Task<ConnectionResult> SuspendAsync()
        {
            try
            {
                PulletClient client = _client;
                return client == null
                    ? ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "PulletNet client has not started.")
                    : await client.SuspendAsync();
            }
            catch (Exception ex) { return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message); }
        }

        public async Task<ConnectionResult> ResumeAsync()
        {
            try
            {
                PulletClient client = _client;
                return client == null
                    ? ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "PulletNet client has not started.")
                    : await client.ResumeAsync();
            }
            catch (Exception ex) { return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message); }
        }

        public ValueTask<SendResult> SendAsync(
            byte[] payload,
            ChannelType channelType = ChannelType.ReliableOrdered,
            CancellationToken cancellationToken = default)
        {
            PulletClient client = _client;
            if (client == null || client.State != ConnectionState.Connected)
                return new ValueTask<SendResult>(SendResult.Rejected(SendErrorCode.NotConnected, "PulletNet client is not connected."));
            return client.SendAsync(payload, channelType, cancellationToken);
        }

        public bool ValidateConfiguration(out string error, string targetHost = null)
            => ValidateConfiguration(targetHost ?? host, tcpPort, udpPort, webSocketPort, out error);

        private bool ValidateConfiguration(
            string targetHost,
            int targetTcpPort,
            int targetUdpPort,
            int targetWebSocketPort,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(targetHost))
            {
                error = "Host cannot be empty.";
                return false;
            }

            bool usesTcp = connectionMode == ConnectionMode.TcpOnly || connectionMode == ConnectionMode.UdpWithTcpControl;
            bool usesUdp = connectionMode == ConnectionMode.UdpOnly || connectionMode == ConnectionMode.UdpWithTcpControl || connectionMode == ConnectionMode.UdpWithWebSocketControl;
            bool usesWebSocket = connectionMode == ConnectionMode.WebSocketOnly || connectionMode == ConnectionMode.UdpWithWebSocketControl;
            if ((usesTcp && !IsValidPort(targetTcpPort)) || (usesUdp && !IsValidPort(targetUdpPort)) ||
                (usesWebSocket && !IsValidPort(targetWebSocketPort)))
            {
                error = "Ports used by the selected connection mode must be between 1 and 65535.";
                return false;
            }
            if (usesWebSocket && (string.IsNullOrWhiteSpace(webSocketPath) || webSocketPath[0] != '/'))
            {
                error = "WebSocket path must start with '/'.";
                return false;
            }
            if (enableDiscovery && !IsValidPort(discoveryPort))
            {
                error = "Discovery port must be between 1 and 65535.";
                return false;
            }
            if (enableDiscovery && (discoveryMaxAttempts <= 0 || discoveryTimeoutSeconds <= 0f ||
                                    discoverySendIntervalSeconds <= 0f))
            {
                error = "Discovery timeout, attempt count and send interval must be greater than zero.";
                return false;
            }
            if (payloadQueueCapacity <= 0 || maxPayloadCallbacksPerFrame <= 0)
            {
                error = "Payload queue capacity and callbacks per frame must be greater than zero.";
                return false;
            }
            error = null;
            return true;
        }

        private async Task<ConnectionResult> ConnectEndpointAsync(
            string targetHost, int targetTcpPort, int targetUdpPort, int targetWebSocketPort, PulletServerInfo server)
        {
            if (!ValidateConfiguration(targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort, out string validationError))
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState, validationError);
            if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");

            try
            {
                ReleaseDisconnectedClient();
                PulletClient client = GetOrCreateClient(targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort);
                ConnectionResult result = await client.ConnectAsync(_lifetimeCts.Token);
                if (result.IsSuccess)
                {
                    ActiveServer = server;
                    host = targetHost;
                    tcpPort = targetTcpPort;
                    udpPort = targetUdpPort;
                    webSocketPort = targetWebSocketPort;
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "PulletNet connection was cancelled.");
            }
            catch (ObjectDisposedException)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message);
            }
        }

        private PulletClient GetOrCreateClient(string targetHost, int targetTcpPort, int targetUdpPort, int targetWebSocketPort)
        {
            lock (_clientGate)
            {
                if (_isShuttingDown) throw new ObjectDisposedException(nameof(PulletNetworkManager));
                bool sameEndpoint = _client != null && string.Equals(_activeHost, targetHost, StringComparison.OrdinalIgnoreCase) &&
                                    _activeTcpPort == targetTcpPort && _activeUdpPort == targetUdpPort &&
                                    _activeWebSocketPort == targetWebSocketPort;
                if (sameEndpoint) return _client;
                if (_client != null) throw new InvalidOperationException("Disconnect the current client before changing endpoint.");

                var options = CreateClientOptions(targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort);
                _client = new PulletClient(options);
                _activeHost = targetHost;
                _activeTcpPort = targetTcpPort;
                _activeUdpPort = targetUdpPort;
                _activeWebSocketPort = targetWebSocketPort;
                BindClientEvents(_client);
                return _client;
            }
        }

        private NetClientOptions CreateClientOptions(string targetHost, int targetTcpPort, int targetUdpPort, int targetWebSocketPort)
        {
            return new NetClientOptions
            {
                Connection = new ConnectionOptions
                {
                    Host = targetHost,
                    TcpPort = targetTcpPort,
                    UdpPort = targetUdpPort,
                    WsPort = targetWebSocketPort,
                    WsPath = webSocketPath,
                    WsUseTls = webSocketUseTls,
                    ConnectionMode = connectionMode
                },
                Version = new VersionOptions
                {
                    ClientVersion = clientVersion,
                    Runtime = (byte)Mathf.Clamp(runtimeId, 0, 255)
                },
                KeepAlive = new KeepAliveOptions { EnableKeepAlive = enableKeepAlive },
                Reconnect = new ReconnectOptions
                {
                    EnableReconnect = enableReconnect,
                    MaxReconnectAttempts = Mathf.Max(0, maxReconnectAttempts)
                }
            };
        }

        private static PulletServerInfo SelectPreferredServer(IReadOnlyList<PulletServerInfo> servers)
        {
            if (servers == null || servers.Count == 0) return null;
            return servers
                .OrderByDescending(s => string.Equals(s.status, "Ready", StringComparison.OrdinalIgnoreCase))
                .ThenBy(s => s.serverName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.instanceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private void BindClientEvents(PulletClient client)
        {
            client.CallbackError += error => Enqueue(() => { if (IsCurrentClient(client)) Debug.LogException(error); });
            client.Connected += value =>
            {
                Enqueue(() =>
                {
                    if (!IsCurrentClient(client)) return;
                    LogInfo($"服务器连接成功：{FormatEndpoint(_activeHost, _activeTcpPort, _activeUdpPort)}，SessionId={value.SessionId}。");
                    Connected?.Invoke(value);
                    OnConnected?.Invoke(false);
                });
            };
            client.Disconnected += value => Enqueue(() =>
            {
                if (!IsCurrentClient(client)) return;
                LogWarning($"服务器连接已断开：Reason={value.ReasonCode}，{value.Reason}");
                Disconnected?.Invoke(value);
                OnDisconnected?.Invoke();
            });
            client.ReconnectAttempt += value => Enqueue(() =>
            {
                if (!IsCurrentClient(client)) return;
                LogInfo($"正在重连服务器：第 {value.Attempt}/{value.MaxAttempts} 次，延迟 {value.DelayMs}ms；原因：{value.Reason}");
                ReconnectAttempt?.Invoke(value);
            });
            client.Reconnected += () => Enqueue(() =>
            {
                if (!IsCurrentClient(client)) return;
                LogInfo($"服务器重连成功：{FormatEndpoint(_activeHost, _activeTcpPort, _activeUdpPort)}。");
                Reconnected?.Invoke();
                OnConnected?.Invoke(true);
            });
            client.ReconnectFailed += value => Enqueue(() =>
            {
                if (!IsCurrentClient(client)) return;
                LogWarning($"服务器重连失败：已尝试 {value.Attempts} 次；原因：{value.Reason}");
                ReconnectFailed?.Invoke(value);
                OnNetworkError?.Invoke(value.Reason);
            });
            client.PayloadReceived += value =>
            {
                if (!IsCurrentClient(client)) return;
                PayloadEnqueueResult result = _payloadQueue.Enqueue(value.ToOwned(), client);
                if (result == PayloadEnqueueResult.ReliableOverflow)
                    Interlocked.Increment(ref _pendingReliablePayloadOverflows);
            };
        }

        private void BindDiscoveryEvents()
        {
            PulletServerDiscovery.OnStarted += HandleDiscoveryStarted;
            PulletServerDiscovery.OnAttempt += HandleDiscoveryAttempt;
            PulletServerDiscovery.OnSuccess += HandleDiscoverySuccess;
            PulletServerDiscovery.OnFailure += HandleDiscoveryFailure;
            PulletServerDiscovery.OnStopped += HandleDiscoveryStopped;
        }

        private void UnbindDiscoveryEvents()
        {
            PulletServerDiscovery.OnStarted -= HandleDiscoveryStarted;
            PulletServerDiscovery.OnAttempt -= HandleDiscoveryAttempt;
            PulletServerDiscovery.OnSuccess -= HandleDiscoverySuccess;
            PulletServerDiscovery.OnFailure -= HandleDiscoveryFailure;
            PulletServerDiscovery.OnStopped -= HandleDiscoveryStopped;
        }

        private void HandleDiscoveryStarted() => Enqueue(PublishDiscoveryStarted);
        private void HandleDiscoveryAttempt(int attempt, int maximum)
            => Enqueue(() => PublishDiscoveryAttempt(attempt, maximum));
        private void HandleDiscoverySuccess(IReadOnlyList<PulletServerInfo> servers)
            => Enqueue(() => PublishDiscoverySuccess(servers));
        private void HandleDiscoveryFailure(string error)
            => Enqueue(() => PublishDiscoveryFailure(error));
        private void HandleDiscoveryStopped() => Enqueue(PublishDiscoveryStopped);

        private void PublishDiscoveryStarted()
        {
            IsDiscovering = true;
            LogInfo($"服务器发现开始：UDP {discoveryPort}，最多广播 {discoveryMaxAttempts} 次，" +
                    $"间隔 {discoverySendIntervalSeconds:0.##} 秒，接收窗口 {discoveryTimeoutSeconds:0.##} 秒。");
            DiscoveryStarted?.Invoke();
            OnDiscoveryStarted?.Invoke();
        }

        private void PublishDiscoveryAttempt(int attempt, int maximum)
        {
            DiscoveryAttempted?.Invoke(attempt, maximum);
            OnDiscoveryAttempt?.Invoke(attempt, maximum);
        }

        private void PublishDiscoverySuccess(IReadOnlyList<PulletServerInfo> servers)
        {
            PulletServerInfo[] snapshot = servers == null ? Array.Empty<PulletServerInfo>() : servers.ToArray();
            LogInfo($"服务器发现成功：共找到 {snapshot.Length} 个匹配服务器。");
            LastDiscoveredServers = snapshot;
            ServersDiscovered?.Invoke(snapshot);
            DiscoverySucceeded?.Invoke(snapshot);
            OnDiscoverySucceeded?.Invoke(snapshot);
        }

        private void PublishDiscoveryFailure(string error)
        {
            string message = string.IsNullOrWhiteSpace(error) ? "Server discovery failed." : error;
            LogWarning("服务器发现失败：" + message);
            DiscoveryFailed?.Invoke(message);
            OnDiscoveryFailed?.Invoke(message);
        }

        private void PublishDiscoveryStopped()
        {
            IsDiscovering = false;
            LogInfo("服务器发现结束。");
            DiscoveryStopped?.Invoke();
            OnDiscoveryStopped?.Invoke();
        }

        private void ReportConnectFailure(ConnectionResult result)
        {
            if (result.IsSuccess || _isShuttingDown) return;
            string message = result.Message ?? "PulletNet connection failed.";
            LogWarning("服务器连接失败：" + message);
            var value = new ConnectFailedEvent(message);
            ConnectFailed?.Invoke(value);
            OnConnectedFailed?.Invoke(message);
        }

        private void LogAutoConnectResult(ConnectionResult result, string target)
        {
            if (result.IsSuccess)
                LogInfo($"自动连接成功：{target}。");
            else
                LogWarning($"自动连接失败：{target}；原因：{FormatResultError(result)}");
        }

        private void LogInfo(string message)
        {
            if (enableConnectionLogs) Debug.Log("[PulletNet] " + message, this);
        }

        private void LogWarning(string message)
        {
            if (enableConnectionLogs) Debug.LogWarning("[PulletNet] " + message, this);
        }

        private static string DescribeAutoConnectMode(AutoConnectMode mode)
        {
            switch (mode)
            {
                case AutoConnectMode.Direct:
                    return "只连接 Inspector 中配置的固定地址";
                case AutoConnectMode.DiscoverFirst:
                    return "先发现局域网服务器，再自动选择连接";
                case AutoConnectMode.DirectThenDiscover:
                    return "先连接固定地址，失败后再启动局域网发现";
                default:
                    return "未知模式";
            }
        }

        private static string FormatEndpoint(string targetHost, int targetTcpPort, int targetUdpPort)
            => $"{targetHost} (TCP {targetTcpPort} / UDP {targetUdpPort})";

        private static string FormatServer(PulletServerInfo server)
        {
            if (server == null) return "<null>";
            string name = string.IsNullOrWhiteSpace(server.serverName)
                ? server.serverId ?? "未命名服务器"
                : server.serverName;
            return $"{name} @ {FormatEndpoint(server.host, server.tcpPort, server.udpPort)}，Status={server.status ?? "Unknown"}";
        }

        private static string FormatResultError(ConnectionResult result)
            => string.IsNullOrWhiteSpace(result.Message) ? result.ErrorCode.ToString() : result.Message;

        private bool IsCurrentClient(PulletClient client) => !_isShuttingDown && ReferenceEquals(client, _client);
        private void Enqueue(Action action) { if (!_isShuttingDown) _mainThreadActions.Enqueue(new MainThreadWorkItem(action)); }

        private void DrainPayloadQueue()
        {
            if (_payloadQueue == null) return;
            int limit = Mathf.Max(1, maxPayloadCallbacksPerFrame);
            for (int i = 0; i < limit && _payloadQueue.TryDequeue(out OwnedPayload owned, out object context); i++)
            {
                using (owned)
                {
                    if (_isShuttingDown || !ReferenceEquals(context, _client)) continue;
                    try
                    {
                        var borrowed = new ReceivedPayload(owned.Memory, owned.TransportType, owned.ChannelType);
                        PayloadReceived?.Invoke(borrowed);
                        OnMessageReceived?.Invoke(owned.ToArray());
                    }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
        }

        private void ResetPayloadQueue()
        {
            _payloadQueue?.Dispose();
            _payloadQueue = new BoundedPayloadQueue(Mathf.Max(1, payloadQueueCapacity));
            Interlocked.Exchange(ref _pendingReliablePayloadOverflows, 0);
        }

        private async Task AutoConnectAndReportAsync()
        {
            ConnectionResult result = await AutoConnectAsync(_lifetimeCts.Token);
            if (!result.IsSuccess && !_isShuttingDown)
                Enqueue(() => ReportConnectFailure(result));
        }

        private async void OnApplicationPause(bool pauseStatus)
        {
            if (_isShuttingDown || !reconnectOnApplicationResume) return;
            try
            {
                if (pauseStatus)
                {
                    _resumeShouldReconnect = IsConnected;
                    if (_resumeShouldReconnect) await SuspendAsync();
                }
                else if (_resumeShouldReconnect)
                {
                    _resumeShouldReconnect = false;
                    ConnectionResult result = await ResumeAsync();
                    if (!result.IsSuccess) Enqueue(() => OnNetworkError?.Invoke(result.Message ?? "PulletNet resume failed."));
                }
            }
            catch (Exception ex) { Enqueue(() => OnNetworkError?.Invoke(ex.Message)); }
        }

        private async void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            _isShuttingDown = true;
            UnbindDiscoveryEvents();
            PulletServerDiscovery.StopDiscovery();
            _lifetimeCts.Cancel();
            try { await DisconnectAsync(); DisposeClient(); }
            catch (Exception ex) { Debug.LogWarning($"[PulletNet] Disconnect during destroy failed: {ex.Message}"); }
            finally
            {
                while (_mainThreadActions.TryDequeue(out var workItem)) workItem.Complete(false);
                _payloadQueue?.Dispose();
                _lifetimeCts.Dispose();
            }
        }

        private void ReleaseDisconnectedClient()
        {
            if (_client == null || (_client.State != ConnectionState.Disconnected &&
                                    _client.State != ConnectionState.Disposed)) return;
            DisposeClient();
        }

        private void DisposeClient()
        {
            PulletClient client;
            lock (_clientGate)
            {
                client = _client;
                _client = null;
                _activeHost = null;
                _activeTcpPort = _activeUdpPort = _activeWebSocketPort = 0;
            }
            if (client == null) return;
            ((IDisposable)client).Dispose();
            _payloadQueue?.Clear();
        }

        private static bool IsValidPort(int port) => port > 0 && port <= 65535;
    }
}
