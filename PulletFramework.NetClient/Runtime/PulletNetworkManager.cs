using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.Messaging;
using PulletNet.ClientSDK;
using PulletClient = PulletNet.ClientSDK.NetClient;
using ConnectionIntent = PulletFramework.NetClient.PulletConnectionCoordinator.ConnectionIntent;
using UnityEngine;

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
        /// <summary>当前进程中生效的网络管理器实例。</summary>
        public static PulletNetworkManager Instance { get; private set; }

        [Header("服务器")]
        [Tooltip("固定连接使用的服务器 IP 地址或主机名。通过局域网发现选择服务器时，会使用发现结果覆盖本次连接目标。")]
        public string host = "127.0.0.1";
        [Tooltip("TCP 数据或控制通道端口。仅 TcpOnly、UdpWithTcpControl 模式使用。")]
        public int tcpPort = 7778;
        [Tooltip("UDP 数据通道端口。仅 UdpOnly、UdpWithTcpControl、UdpWithWebSocketControl 模式使用。")]
        public int udpPort = 7777;
        [Tooltip("WebSocket 数据或控制通道端口。仅 WebSocketOnly、UdpWithWebSocketControl 模式使用。")]
        public int webSocketPort = 7779;
        [Tooltip("WebSocket 握手路径，必须以 / 开头。例如 /ws。")]
        public string webSocketPath = "/ws";
        [Tooltip("是否使用加密 WebSocket（wss）。启用后服务器必须已配置 TLS 证书。")]
        public bool webSocketUseTls;

        [Header("连接")]
        [Tooltip("底层传输组合。XRVehicle 当前使用 UDP 传输实时数据、TCP 传输可靠控制消息。")]
        public ConnectionMode connectionMode = ConnectionMode.UdpWithTcpControl;
        [Tooltip("自动连接策略：Direct=只连固定地址；DiscoverFirst=跳过固定地址并发现后自动择优连接；DirectThenDiscover=固定地址失败后再发现并自动连接。")]
        public AutoConnectMode autoConnectMode = AutoConnectMode.DirectThenDiscover;
        [Tooltip("组件启动时是否立即执行 Auto Connect Mode。关闭后由业务界面调用连接或发现。")]
        public bool connectOnStart = true;
        [Tooltip("切换 Unity 场景时是否保留此网络管理器。场景内明确管理生命周期时应关闭。")]
        public bool dontDestroyOnLoad = true;
        [Tooltip("应用从后台或头显休眠恢复时，如果恢复前处于连接状态，是否尝试恢复连接。")]
        public bool reconnectOnApplicationResume = true;
        [Tooltip("是否启用底层心跳保活，用于及时发现对端不可达或网络断开。")]
        public bool enableKeepAlive = true;
        [Tooltip("连接意外断开后，是否执行 PulletNet 内部快速重连。")]
        public bool enableReconnect = true;
        [Tooltip("PulletNet 内部快速重连全部失败后，是否继续定期连接最后一次明确选择的服务器。不会重新发现或切换服务器。")]
        public bool keepReconnectingActiveServer;
        [Min(0.5f), Tooltip("持续恢复最后服务器时，两次连接尝试之间的间隔（秒）。")]
        public float activeServerReconnectDelaySeconds = 3f;
        [Min(0), Tooltip("PulletNet 内部快速重连的最大尝试次数。设为 0 表示不进行快速重连。")]
        public int maxReconnectAttempts = 10;

        [Header("局域网发现")]
        [Tooltip("是否允许通过 UDP 广播发现局域网内的 PulletNet 服务器。")]
        public bool enableDiscovery = true;
        [Tooltip("发送和接收服务器发现广播的 UDP 端口。客户端与服务器必须一致。")]
        public int discoveryPort = 58888;
        [Min(0.25f), Tooltip("单轮发现的总接收窗口（秒）。到期后汇总本轮找到的服务器。")]
        public float discoveryTimeoutSeconds = 3f;
        [Min(1), Tooltip("单轮发现中最多发送的广播次数。")]
        public int discoveryMaxAttempts = 5;
        [Min(0.1f), Tooltip("同一轮发现中，两次 UDP 广播之间的间隔（秒）。")]
        public float discoverySendIntervalSeconds = 0.5f;
        [Min(0.25f), Tooltip("自动连接策略需要重新发起下一轮发现时，两轮之间等待的时间（秒）。")]
        public float discoveryRetrySeconds = 1f;
        [Tooltip("一轮发现没有找到可连接服务器时，是否继续重复发现。关闭后由业务界面决定何时重试。")]
        public bool retryDiscoveryUntilConnected = true;
        [Tooltip("发现服务类型过滤标识。只接受 Service Type 完全相同的服务器，避免连接到同网段的其他 PulletNet 应用。")]
        public string discoveryServiceType = "pulletnet";

        [Header("主线程事件队列")]
        [Min(1), Tooltip("后台网络线程等待派发到 Unity 主线程的最大 Payload 数量。队列长期满载说明主线程处理不过来。")]
        public int payloadQueueCapacity = 1024;
        [Min(1), Tooltip("每帧最多在 Unity 主线程执行的 Payload 回调数量。数值过小会增加延迟，过大会造成单帧卡顿。")]
        public int maxPayloadCallbacksPerFrame = 256;

        [Header("日志")]
        [Tooltip("输出连接模式、服务器发现、连接结果及重连状态等关键日志。不会输出每个数据包。")]
        public bool enableConnectionLogs = true;

        private readonly PulletMainThreadEventPump _eventPump = new PulletMainThreadEventPump();
        private readonly object _clientGate = new object();
        private readonly PulletConnectionCoordinator _connectionCoordinator = new PulletConnectionCoordinator();
        private PulletClient _client;
        private PulletMessageClient _messages;
        private CancellationTokenSource _lifetimeCts;
        private bool _resumeShouldReconnect;
        private volatile bool _isShuttingDown;
        private int _autoConnectRunning;
        private PulletActiveServerRecovery _activeServerRecovery;
        private string _activeHost;
        private int _activeTcpPort;
        private int _activeUdpPort;
        private int _activeWebSocketPort;

        /// <summary>底层传输已完成握手，可安全发送应用消息。</summary>
        public bool IsConnected => _client != null && _client.State == ConnectionState.Connected;
        /// <summary>当前是否正在执行一轮局域网服务器发现。</summary>
        public bool IsDiscovering { get; private set; }
        /// <summary>最近一次成功连接或明确选择的服务器。</summary>
        public PulletServerInfo ActiveServer { get; private set; }
        public IReadOnlyList<PulletServerInfo> LastDiscoveredServers { get; private set; } = Array.Empty<PulletServerInfo>();
        public long DroppedPayloadCount => _eventPump.DroppedPayloadCount;
        public long ReliablePayloadOverflowCount => _eventPump.ReliablePayloadOverflowCount;
        public bool IsMessagingConfigured => _messages != null;
        /// <summary>是否正在持续恢复最后一次选择的服务器。</summary>
        public bool IsRecoveringActiveServer => _activeServerRecovery != null && _activeServerRecovery.IsRunning;

        /// <summary>首次连接成功。重连成功使用 <see cref="Reconnected"/>。</summary>
        public event Action<ConnectedEvent> Connected;
        /// <summary>一次显式连接请求失败。</summary>
        public event Action<ConnectFailedEvent> ConnectFailed;
        /// <summary>现有连接已断开。</summary>
        public event Action<DisconnectedEvent> Disconnected;
        /// <summary>原始应用 Payload。强类型业务优先使用 <see cref="Subscribe{T}"/>。</summary>
        public event Action<ReceivedPayload> PayloadReceived;
        /// <summary>主线程 Payload 队列丢失了可靠消息，应用数据可能不完整。</summary>
        public event Action<string> PayloadQueueFaulted;
        /// <summary>PulletNet 内部快速重连正在进行。</summary>
        public event Action<ReconnectAttemptEvent> ReconnectAttempt;
        /// <summary>PulletNet 内部快速重连成功。</summary>
        public event Action Reconnected;
        /// <summary>PulletNet 内部快速重连已耗尽。</summary>
        public event Action<ReconnectFailedEvent> ReconnectFailed;
        /// <summary>持续恢复最后服务器的尝试次数。该阶段发生在内部快速重连耗尽之后。</summary>
        public event Action<int> ActiveServerReconnectAttempt;
        /// <summary>局域网发现开始。</summary>
        public event Action DiscoveryStarted;
        /// <summary>局域网发现发送了一次广播，参数为当前次数和最大次数。</summary>
        public event Action<int, int> DiscoveryAttempted;
        /// <summary>局域网发现结束并获得至少一个匹配服务器。</summary>
        public event Action<IReadOnlyList<PulletServerInfo>> DiscoverySucceeded;
        /// <summary>局域网发现失败或未找到匹配服务器。</summary>
        public event Action<string> DiscoveryFailed;
        /// <summary>局域网发现流程已完全停止。</summary>
        public event Action DiscoveryStopped;
        /// <summary>每次发现成功时发布服务器快照；保留用于服务器选择 UI。</summary>
        public event Action<IReadOnlyList<PulletServerInfo>> ServersDiscovered;
        /// <summary>收到合法但没有业务订阅者的消息。</summary>
        public event Action<uint> UnhandledBusinessMessage;
        /// <summary>业务信封、序列化或 RPC 响应出现协议错误。</summary>
        public event Action<string> MessageProtocolError;

        /// <summary>
        /// 为当前应用配置一个业务消息协议。serializer 为空时使用 SDK 内置 JSON。
        /// 应在订阅消息或建立连接前调用一次。
        /// </summary>
        public void ConfigureMessaging(
            PulletMessageProtocolOptions protocol,
            IMessageSerializer serializer = null)
        {
            if (protocol == null) throw new ArgumentNullException(nameof(protocol));
            PulletMessageClient replacement = new PulletMessageClient(
                protocol,
                serializer ?? new JsonMessageSerializer(),
                (payload, channel, cancellationToken) => SendAsync(payload, channel, cancellationToken));
            replacement.UnhandledMessage += ForwardUnhandledBusinessMessage;
            replacement.ProtocolError += ForwardMessageProtocolError;

            PulletMessageClient previous = _messages;
            _messages = replacement;
            if (previous != null)
            {
                previous.UnhandledMessage -= ForwardUnhandledBusinessMessage;
                previous.ProtocolError -= ForwardMessageProtocolError;
                previous.Dispose();
            }
        }

        /// <summary>订阅指定业务消息。返回值必须在模块停用或销毁时 Dispose。</summary>
        public IMessageSubscription Subscribe<T>(uint messageId, Action<T> handler)
            => GetMessages().Subscribe(messageId, handler);

        /// <summary>显式取消订阅；也可以直接调用订阅句柄的 Unsubscribe 或 Dispose。</summary>
        public bool Unsubscribe(IMessageSubscription subscription)
            => _messages != null && _messages.Unsubscribe(subscription);

        /// <summary>发送无响应的强类型业务通知。</summary>
        public Task SendMessageAsync<T>(
            uint messageId,
            T value,
            ChannelType channel = ChannelType.ReliableOrdered,
            CancellationToken cancellationToken = default)
            => GetMessages().SendAsync(messageId, value, channel, cancellationToken);

        /// <summary>发送请求并等待 correlationId 匹配的响应、超时或取消。</summary>
        public Task<TResponse> CallAsync<TRequest, TResponse>(
            uint requestId,
            uint responseId,
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => GetMessages().CallAsync<TRequest, TResponse>(
                requestId, responseId, request, timeout, cancellationToken);

        /// <summary>取消属于当前会话的全部未完成 RPC。断线时会自动调用。</summary>
        public void CancelPendingMessages(string reason)
        {
            _messages?.CancelPending(reason);
        }

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
            _activeServerRecovery = new PulletActiveServerRecovery(
                () => IsConnected,
                (target, token) => ConnectEndpointAsync(
                    target.host, target.tcpPort, target.udpPort, target.webPort, target,
                    token, _connectionCoordinator.Version),
                ReleaseDisconnectedClient,
                () => !_isShuttingDown,
                attempt => ActiveServerReconnectAttempt?.Invoke(attempt),
                LogInfo,
                LogWarning);
            _eventPump.Reset(payloadQueueCapacity);
            BindDiscoveryEvents();
        }

        private async void Start()
        {
            if (connectOnStart)
                await AutoConnectAndReportAsync();
        }

        private void Update()
        {
            _eventPump.DrainActions(!_isShuttingDown, ex => Debug.LogException(ex));
            _eventPump.DrainPayloads(
                maxPayloadCallbacksPerFrame,
                context => !_isShuttingDown && ReferenceEquals(context, _client),
                payload => _messages?.HandlePayload(payload, out _),
                payload => PayloadReceived?.Invoke(payload),
                ex => Debug.LogException(ex));
            int reliableOverflows = _eventPump.TakeReliableOverflowCount();
            if (reliableOverflows <= 0) return;

            string error = $"Reliable payload queue overflowed {reliableOverflows} time(s); application delivery is no longer complete.";
            Debug.LogError($"[PulletNet] {error}");
            try
            {
                PayloadQueueFaulted?.Invoke(error);
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>连接 Inspector 中配置的固定端点。</summary>
        public async Task<ConnectionResult> ConnectAsync()
        {
            return await ConnectManualAsync(host, tcpPort, udpPort, webSocketPort, null,
                $"手动连接固定服务器：{FormatEndpoint(host, tcpPort, udpPort)}");
        }

        /// <summary>使用当前端口配置连接指定主机。</summary>
        public async Task<ConnectionResult> ConnectAsync(string targetHost)
        {
            return await ConnectManualAsync(targetHost, tcpPort, udpPort, webSocketPort, null,
                $"手动连接指定服务器：{FormatEndpoint(targetHost, tcpPort, udpPort)}");
        }

        /// <summary>连接服务器发现结果中的明确目标，并将其记为最后选择的服务器。</summary>
        public async Task<ConnectionResult> ConnectAsync(PulletServerInfo server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return await ConnectManualAsync(
                server.host,
                server.tcpPort > 0 ? server.tcpPort : tcpPort,
                server.udpPort > 0 ? server.udpPort : udpPort,
                server.webPort > 0 ? server.webPort : webSocketPort,
                server,
                $"连接已选择的服务器：{FormatServer(server)}");
        }

        private async Task<ConnectionResult> ConnectManualAsync(
            string targetHost, int targetTcpPort, int targetUdpPort, int targetWebSocketPort,
            PulletServerInfo server, string description)
        {
            if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");
            if (IsConnected)
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState,
                    "Disconnect the current server before starting another connection.");
            ConnectionIntent intent;
            try { intent = BeginConnectionIntent(); }
            catch (ObjectDisposedException)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed,
                    "PulletNet client is shutting down.");
            }
            try
            {
                LogInfo(description);
                ConnectionResult result = await ConnectEndpointAsync(
                    targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort, server,
                    intent.Token, intent.Version);
                if (!result.IsSuccess && IsCurrentIntent(intent.Version) && !intent.Token.IsCancellationRequested)
                    Enqueue(() => { if (IsCurrentIntent(intent.Version)) ReportConnectFailure(result); });
                return result;
            }
            finally { CompleteConnectionIntent(intent); }
        }

        /// <summary>按照 AutoConnectMode 执行固定端点连接与局域网发现策略。</summary>
        public async Task<ConnectionResult> AutoConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected) return ConnectionResult.Ok();
            if (Interlocked.Exchange(ref _autoConnectRunning, 1) != 0)
            {
                LogWarning("自动连接请求被忽略：已有自动连接流程正在运行。");
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "Auto connect is already running.");
            }

            try
            {
                if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                    return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");
                ConnectionIntent intent = BeginConnectionIntent(cancellationToken);
                try
                {
                    var strategy = new PulletAutoConnectStrategy(
                        token => ConnectEndpointAsync(host, tcpPort, udpPort, webSocketPort, null,
                            token, intent.Version),
                        DiscoverAsync,
                        (server, token) => ConnectEndpointAsync(
                            server.host,
                            server.tcpPort > 0 ? server.tcpPort : tcpPort,
                            server.udpPort > 0 ? server.udpPort : udpPort,
                            server.webPort > 0 ? server.webPort : webSocketPort,
                            server, token, intent.Version),
                        ReleaseDisconnectedClient,
                        () => !_isShuttingDown,
                        LogInfo,
                        LogWarning);
                    return await strategy.RunAsync(new PulletAutoConnectStrategy.Options
                    {
                        Mode = autoConnectMode,
                        EnableDiscovery = enableDiscovery,
                        RetryDiscoveryUntilConnected = retryDiscoveryUntilConnected,
                        DiscoveryRetrySeconds = discoveryRetrySeconds,
                        FixedEndpointDescription = FormatEndpoint(host, tcpPort, udpPort),
                        DiscoveryDescription = $"UDP {discoveryPort}，ServiceType={discoveryServiceType}"
                    }, intent.Token);
                }
                finally { CompleteConnectionIntent(intent); }
            }
            catch (OperationCanceledException)
            {
                LogWarning("自动连接已取消。");
                return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Auto connect was cancelled.");
            }
            catch (ObjectDisposedException)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");
            }
            finally
            {
                Interlocked.Exchange(ref _autoConnectRunning, 0);
            }
        }

        /// <summary>执行一轮可等待的局域网发现，返回这一轮的完整服务器快照。</summary>
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
                Debug.LogException(ex, this);
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
        /// <summary>启动事件驱动的局域网发现；已有发现运行时返回 false。</summary>
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

        /// <summary>停止事件驱动的局域网发现。停止属于正常取消。</summary>
        public void StopDiscovery() => PulletServerDiscovery.StopDiscovery();

        /// <summary>主动断开当前连接，同时终止旧端点的持续恢复。</summary>
        public async Task<ConnectionResult> DisconnectAsync()
        {
            long intentVersion = BeginDisconnectIntent();
            bool acquired = false;
            try
            {
                await _connectionCoordinator.OperationGate.WaitAsync();
                acquired = true;
                if (!_connectionCoordinator.IsCurrent(intentVersion))
                    return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Disconnect was superseded.");
                PulletClient client = _client;
                ConnectionResult result = client == null ? ConnectionResult.Ok() : await client.DisconnectAsync();
                return result;
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message);
            }
            finally { if (acquired) _connectionCoordinator.OperationGate.Release(); }
        }

        /// <summary>应用暂停时挂起底层网络会话。</summary>
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

        /// <summary>恢复此前挂起的底层网络会话。</summary>
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

        /// <summary>发送原始 Payload。普通业务优先使用 SendMessageAsync 或 CallAsync。</summary>
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

        /// <summary>验证当前连接、发现和队列配置是否可以启动。</summary>
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
            string targetHost, int targetTcpPort, int targetUdpPort, int targetWebSocketPort,
            PulletServerInfo server, CancellationToken cancellationToken, long intentVersion)
        {
            if (!ValidateConfiguration(targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort, out string validationError))
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState, validationError);
            if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");

            bool acquired = false;
            try
            {
                await _connectionCoordinator.OperationGate.WaitAsync(cancellationToken);
                acquired = true;
                if (!IsCurrentIntent(intentVersion) || cancellationToken.IsCancellationRequested)
                    return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Connection was superseded.");
                ReleaseDisconnectedClient();
                PulletClient client = GetOrCreateClient(
                    targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort, intentVersion);
                ConnectionResult result = await client.ConnectAsync(cancellationToken);
                if (!IsCurrentIntent(intentVersion) || cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (client.State == ConnectionState.Connected)
                            await client.DisconnectAsync();
                    }
                    catch (Exception ex) { LogWarning("清理过期连接失败：" + ex.Message); }
                    finally { DisposeClient(); }
                    return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Connection was superseded.");
                }
                if (result.IsSuccess)
                {
                    ActiveServer = server;
                    host = targetHost;
                    tcpPort = targetTcpPort;
                    udpPort = targetUdpPort;
                    webSocketPort = targetWebSocketPort;
                }
                else ReleaseDisconnectedClient();
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
            finally { if (acquired) _connectionCoordinator.OperationGate.Release(); }
        }

        private PulletClient GetOrCreateClient(
            string targetHost, int targetTcpPort, int targetUdpPort, int targetWebSocketPort, long intentVersion)
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
                BindClientEvents(_client, intentVersion);
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
                KeepAlive = new KeepAliveOptions { EnableKeepAlive = enableKeepAlive },
                Reconnect = new ReconnectOptions
                {
                    EnableReconnect = enableReconnect,
                    MaxReconnectAttempts = Mathf.Max(0, maxReconnectAttempts)
                }
            };
        }

        private void BindClientEvents(PulletClient client, long intentVersion)
        {
            client.CallbackError += error => Enqueue(() =>
            {
                if (IsCurrentClient(client, intentVersion)) Debug.LogException(error);
            });
            client.Connected += value => HandleClientConnected(client, intentVersion, value);
            client.Disconnected += value => HandleClientDisconnected(client, value);
            client.ReconnectAttempt += value => Enqueue(() =>
            {
                if (!IsCurrentClient(client, intentVersion)) return;
                LogInfo($"正在重连服务器：第 {value.Attempt}/{value.MaxAttempts} 次，延迟 {value.DelayMs}ms；原因：{value.Reason}");
                ReconnectAttempt?.Invoke(value);
            });
            client.Reconnected += () => HandleClientReconnected(client, intentVersion);
            client.ReconnectFailed += value => Enqueue(() =>
            {
                if (!IsCurrentClient(client, intentVersion)) return;
                LogWarning($"服务器重连失败：已尝试 {value.Attempts} 次；原因：{value.Reason}");
                ReconnectFailed?.Invoke(value);
                if (IsCurrentClient(client, intentVersion))
                    BeginPersistentActiveServerReconnect(value.Reason);
            });
            client.PayloadReceived += value =>
            {
                if (!IsCurrentClient(client, intentVersion)) return;
                _eventPump.EnqueuePayload(value, client);
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

        // 生命周期事件只在接收时确认归属。确认后即使切换 Client，也按入队顺序交付。
        internal void HandleClientConnected(PulletClient client, long intentVersion, ConnectedEvent value)
        {
            if (!IsCurrentClient(client, intentVersion)) return;
            string endpoint = FormatEndpoint(_activeHost, _activeTcpPort, _activeUdpPort);
            Enqueue(() =>
            {
                LogInfo($"服务器连接成功：{endpoint}，SessionId={value.SessionId}。");
                Connected?.Invoke(value);
            });
        }

        internal void HandleClientDisconnected(PulletClient client, DisconnectedEvent value)
        {
            if (!IsCurrentClient(client)) return;
            InvalidateSession("PulletNet transport disconnected: " + value.Reason);
            Enqueue(() =>
            {
                LogWarning($"服务器连接已断开：Reason={value.ReasonCode}，{value.Reason}");
                Disconnected?.Invoke(value);
            });
        }

        internal void HandleClientReconnected(PulletClient client, long intentVersion)
        {
            if (!IsCurrentClient(client, intentVersion)) return;
            string endpoint = FormatEndpoint(_activeHost, _activeTcpPort, _activeUdpPort);
            Enqueue(() =>
            {
                LogInfo($"服务器重连成功：{endpoint}。");
                Reconnected?.Invoke();
            });
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
        }

        private void PublishDiscoveryAttempt(int attempt, int maximum)
        {
            DiscoveryAttempted?.Invoke(attempt, maximum);
        }

        private void PublishDiscoverySuccess(IReadOnlyList<PulletServerInfo> servers)
        {
            PulletServerInfo[] snapshot = servers == null ? Array.Empty<PulletServerInfo>() : servers.ToArray();
            LogInfo($"服务器发现成功：共找到 {snapshot.Length} 个匹配服务器。");
            LastDiscoveredServers = snapshot;
            ServersDiscovered?.Invoke(snapshot);
            DiscoverySucceeded?.Invoke(snapshot);
        }

        private void PublishDiscoveryFailure(string error)
        {
            string message = string.IsNullOrWhiteSpace(error) ? "Server discovery failed." : error;
            LogWarning("服务器发现失败：" + message);
            DiscoveryFailed?.Invoke(message);
        }

        private void PublishDiscoveryStopped()
        {
            IsDiscovering = false;
            LogInfo("服务器发现结束。");
            DiscoveryStopped?.Invoke();
        }

        private void ReportConnectFailure(ConnectionResult result)
        {
            if (result.IsSuccess || _isShuttingDown) return;
            string message = result.Message ?? "PulletNet connection failed.";
            LogWarning("服务器连接失败：" + message);
            var value = new ConnectFailedEvent(message);
            ConnectFailed?.Invoke(value);
        }

        private void LogInfo(string message)
        {
            if (enableConnectionLogs) Debug.Log("[PulletNet] " + message, this);
        }

        private void LogWarning(string message)
        {
            if (enableConnectionLogs) Debug.LogWarning("[PulletNet] " + message, this);
        }

        private static string FormatEndpoint(string targetHost, int targetTcpPort, int targetUdpPort)
            => $"{targetHost} (TCP {targetTcpPort} / UDP {targetUdpPort})";

        private static string FormatServer(PulletServerInfo server)
        {
            if (server == null) return "<null>";
            string name = string.IsNullOrWhiteSpace(server.serverName)
                ? server.serverId ?? "未命名服务器"
                : server.serverName;
            return $"{name} @ {FormatEndpoint(server.host, server.tcpPort, server.udpPort)}";
        }

        private static string FormatResultError(ConnectionResult result)
            => string.IsNullOrWhiteSpace(result.Message) ? result.ErrorCode.ToString() : result.Message;

        private bool IsCurrentClient(PulletClient client) => !_isShuttingDown && ReferenceEquals(client, _client);
        private bool IsCurrentClient(PulletClient client, long intentVersion)
            => IsCurrentClient(client) && IsCurrentIntent(intentVersion);
        private void Enqueue(Action action) { if (!_isShuttingDown) _eventPump.Enqueue(action); }

        private PulletMessageClient GetMessages()
        {
            if (_messages == null)
                throw new InvalidOperationException(
                    "Messaging is not configured. Call ConfigureMessaging before using typed messages.");
            return _messages;
        }

        private void ForwardUnhandledBusinessMessage(uint messageId)
        {
            UnhandledBusinessMessage?.Invoke(messageId);
        }

        private void ForwardMessageProtocolError(string error)
        {
            MessageProtocolError?.Invoke(error);
        }

        /// <summary>每次明确连接或断开都会取消旧请求，并使旧请求的回调失效。</summary>
        private ConnectionIntent BeginConnectionIntent(CancellationToken requestToken = default)
        {
            _activeServerRecovery?.Cancel();
            return _connectionCoordinator.BeginConnection(_lifetimeCts.Token, requestToken);
        }

        private long BeginDisconnectIntent()
        {
            _activeServerRecovery?.Cancel();
            return _connectionCoordinator.BeginDisconnect();
        }

        private void CompleteConnectionIntent(ConnectionIntent intent)
            => _connectionCoordinator.Complete(intent);

        private bool IsCurrentIntent(long version)
            => !_isShuttingDown && _connectionCoordinator.IsCurrent(version);

        private void InvalidateSession(string reason)
        {
            _eventPump.InvalidateSession();
            _messages?.CancelPending(reason);
        }

        private void BeginPersistentActiveServerReconnect(string reason)
        {
            if (!keepReconnectingActiveServer || _isShuttingDown || _activeServerRecovery == null ||
                _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return;

            PulletServerInfo target = SnapshotActiveServer();
            if (target == null || string.IsNullOrWhiteSpace(target.host))
            {
                LogWarning("持续恢复已启用，但没有可恢复的服务器端点。");
                return;
            }
            _activeServerRecovery.Start(
                target,
                activeServerReconnectDelaySeconds,
                reason,
                _lifetimeCts.Token);
        }

        private PulletServerInfo SnapshotActiveServer()
        {
            PulletServerInfo source = ActiveServer;
            return new PulletServerInfo
            {
                instanceId = source?.instanceId,
                serverId = source?.serverId,
                serverName = source?.serverName,
                serviceType = source?.serviceType,
                host = string.IsNullOrWhiteSpace(_activeHost) ? source?.host : _activeHost,
                tcpPort = _activeTcpPort > 0 ? _activeTcpPort : source?.tcpPort ?? tcpPort,
                udpPort = _activeUdpPort > 0 ? _activeUdpPort : source?.udpPort ?? udpPort,
                webPort = _activeWebSocketPort > 0 ? _activeWebSocketPort : source?.webPort ?? webSocketPort
            };
        }

        private async Task AutoConnectAndReportAsync()
        {
            ConnectionResult result = await AutoConnectAsync(_lifetimeCts.Token);
            if (!result.IsSuccess && result.ErrorCode != ConnectionErrorCode.Canceled && !_isShuttingDown)
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
                    if (!result.IsSuccess) Enqueue(() => LogWarning(result.Message ?? "PulletNet resume failed."));
                }
            }
            catch (Exception ex) { Enqueue(() => LogWarning(ex.Message)); }
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
                _activeServerRecovery?.Dispose();
                _activeServerRecovery = null;
                if (_messages != null)
                {
                    _messages.UnhandledMessage -= ForwardUnhandledBusinessMessage;
                    _messages.ProtocolError -= ForwardMessageProtocolError;
                    _messages.Dispose();
                    _messages = null;
                }
                _eventPump.Dispose();
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
            InvalidateSession("PulletNet client was replaced or disposed.");
            ((IDisposable)client).Dispose();
        }

        private static bool IsValidPort(int port) => port > 0 && port <= 65535;
    }
}
