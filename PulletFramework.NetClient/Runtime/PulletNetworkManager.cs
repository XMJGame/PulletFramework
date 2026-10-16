using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.Messaging;
using PulletNet.ClientSDK;
using PulletClient = PulletNet.ClientSDK.NetClient;
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
        [Tooltip("TCP 端口。仅 TcpOnly 模式使用。")]
        public int tcpPort = 7778;
        [Tooltip("UDP 端口。仅 UdpOnly 模式使用，握手与数据均走 UDP。")]
        public int udpPort = 7777;
        [Tooltip("WebSocket 端口。仅 WebSocketOnly 模式使用。")]
        public int webSocketPort = 7779;
        [Tooltip("WebSocket 握手路径，必须以 / 开头。例如 /ws。")]
        public string webSocketPath = "/ws";
        [Tooltip("是否使用加密 WebSocket（wss）。启用后服务器必须已配置 TLS 证书。")]
        public bool webSocketUseTls;

        [Header("连接")]
        [Tooltip("单连接传输：UdpOnly、TcpOnly 或 WebSocketOnly。旧组合模式值 2/3 必须显式迁移，不会自动降级。")]
        public ConnectionMode connectionMode = ConnectionMode.UdpOnly;
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
        [Min(1), Tooltip("准备、排队及执行回调的 Payload 有效字节总上限，不等同于进程内存。")]
        public int payloadQueueBytes = 4 * 1024 * 1024;
        [Min(1), Tooltip("单帧 Payload 有效字节预算。超过完整单帧预算的单条消息在复制前拒收；可靠交付因此失败时关闭默认来源。")]
        public int maxPayloadBytesPerFrame = 1024 * 1024;

        [Header("日志")]
        [Tooltip("输出连接模式、服务器发现、连接结果及重连状态等关键日志。不会输出每个数据包。")]
        public bool enableConnectionLogs = true;

        private readonly object _clientGate = new object();
        private readonly PulletConnectionCoordinator _connectionCoordinator = new PulletConnectionCoordinator();
        // The Runtime/SDK host is the only client owner. These are borrowed handles.
        private PulletClientRuntime _runtime;
        private int _unityThread;
        private readonly PulletClientRuntime.Subscribers<ReceivedPayload> _payloadHandlers = new PulletClientRuntime.Subscribers<ReceivedPayload>();
        private HostedClient _source;
        private PulletClient _client => _source?.Client;
        private Action<ConnectionStateChangedEvent> _sessionObserver;
        private const int MaximumOperations = 32, MaximumApplicationEvents = 128, ApplicationEventsPerFrame = 64;
        private readonly Queue<Action> _applicationEvents = new Queue<Action>();
        private TaskCompletionSource<bool> _operationsExited = CompletedSignal();
        private int _operations, _applicationCallbacks;
        private Task _shutdown;
        private Operation _discovery;
        private Exception _applicationQueueFailure;
        private long _reliableOverflows;
        private PulletMessageClient _messages;
        private CancellationTokenSource _lifetimeCts;
        private bool _resumeShouldReconnect;
        private int _applicationPauseReasons;
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
        public long DroppedPayloadCount => _runtime?.GetSnapshot().DroppedPayloads ?? 0;
        public long ReliablePayloadOverflowCount => Interlocked.Read(ref _reliableOverflows);
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
        public event Action<ReceivedPayload> PayloadReceived { add => _payloadHandlers.Add(value); remove => _payloadHandlers.Remove(value); }
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
            lock (_clientGate)
            {
                if (_isShuttingDown) throw new ObjectDisposedException(nameof(PulletNetworkManager));
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
        }

        /// <summary>订阅指定业务消息。返回值必须在模块停用或销毁时 Dispose。</summary>
        public IMessageSubscription Subscribe<T>(uint messageId, Action<T> handler)
            => GetMessages().Subscribe(messageId, handler);

        /// <summary>根据消息类型上的契约订阅业务 Notification。</summary>
        public IMessageSubscription Subscribe<T>(Action<T> handler)
            => GetMessages().Subscribe(handler);

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

        /// <summary>根据消息类型上的契约发送无响应的业务 Notification。</summary>
        public Task SendMessageAsync<T>(
            T value,
            ChannelType channel = ChannelType.ReliableOrdered,
            CancellationToken cancellationToken = default)
            => GetMessages().SendAsync(value, channel, cancellationToken);

        /// <summary>发送请求并等待 RpcId 匹配的响应、超时或取消。</summary>
        public Task<TResponse> CallAsync<TRequest, TResponse>(
            uint requestId,
            uint responseId,
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => GetMessages().CallAsync<TRequest, TResponse>(
                requestId, responseId, request, timeout, cancellationToken);

        /// <summary>根据请求和响应类型上的契约完成一次 RPC。</summary>
        public Task<TResponse> CallAsync<TRequest, TResponse>(
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => GetMessages().CallAsync<TRequest, TResponse>(request, timeout, cancellationToken);

        /// <summary>取消属于当前会话的全部未完成 RPC。断线时会自动调用。</summary>
        public void CancelPendingMessages(string reason)
        {
            _messages?.CancelPending(reason);
        }

        private void Awake()
        {
            if (_lifetimeCts != null || _isShuttingDown) return;
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _unityThread = Thread.CurrentThread.ManagedThreadId;
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
            _lifetimeCts = new CancellationTokenSource();
            _activeServerRecovery = new PulletActiveServerRecovery(
                () => IsConnected,
                (target, token) => ConnectEndpointAsync(
                    target.host, target.tcpPort, target.udpPort, target.webPort, target,
                    token, _connectionCoordinator.Version),
                ReleaseDisconnectedClientAsync,
                () => !_isShuttingDown,
                attempt => Enqueue(() => InvokeEach(ActiveServerReconnectAttempt, attempt)),
                LogInfo,
                LogWarning);
            // Configuration is frozen when the first connection is created, so
            // scripts may still set Inspector-equivalent values before connecting.
        }

        private void Start()
        {
            if (connectOnStart)
                ObserveRequest(AutoConnectAndReportAsync());
        }

        private void Update()
        {
            for (int i = 0; i < ApplicationEventsPerFrame; i++)
            {
                Action action;
                lock (_clientGate)
                {
                    if (_isShuttingDown || _applicationEvents.Count == 0) break;
                    action = _applicationEvents.Dequeue(); _applicationCallbacks++;
                    BeginOperationCount();
                }
                try { action(); } catch (Exception error) { ReportCallbackError(error); }
                finally { lock (_clientGate) { _applicationCallbacks--; EndOperationCount(); } }
            }
            _runtime?.Update();
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
            using var operation = EnterOperation();
            if (operation == null) return ShuttingDown();
            if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");
            if (IsConnected)
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState,
                    "Disconnect the current server before starting another connection.");
            PulletConnectionCoordinator.ConnectionIntent intent;
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
            using var operation = EnterOperation(cancellationToken);
            if (operation == null) return ShuttingDown();
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
                PulletConnectionCoordinator.ConnectionIntent intent = BeginConnectionIntent(operation.Token);
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
                        ReleaseDisconnectedClientAsync,
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
            using var operation = EnterOperation(cancellationToken);
            if (operation == null) return Array.Empty<PulletServerInfo>();
            if (!enableDiscovery)
                return Array.Empty<PulletServerInfo>();
            if (!IsValidPort(discoveryPort))
                throw new InvalidOperationException("Discovery port must be between 1 and 65535.");

            lock (_clientGate)
            {
                if (_discovery != null) throw new InvalidOperationException("A discovery round is already running.");
                _discovery = operation; IsDiscovering = true;
            }
            try
            {
                Enqueue(PublishDiscoveryStarted);
                IReadOnlyList<PulletServerInfo> servers = await PulletServerDiscovery.DiscoverAsync(
                    discoveryPort,
                    discoveryTimeoutSeconds,
                    discoveryMaxAttempts,
                    discoverySendIntervalSeconds,
                    discoveryServiceType,
                    (attempt, maximum) => Enqueue(() => PublishDiscoveryAttempt(attempt, maximum)),
                    operation.Token);
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
                lock (_clientGate) { _discovery = null; IsDiscovering = false; }
                Enqueue(PublishDiscoveryStopped);
            }
        }

        /// <summary>启动事件驱动的局域网发现；已有发现运行时返回 false。</summary>
        public bool StartDiscovery()
        {
            lock (_clientGate)
            {
                if (_isShuttingDown || _lifetimeCts == null || _discovery != null || !enableDiscovery) return false;
            }
            ObserveRequest(DiscoverAsync());
            return IsDiscovering;
        }

        /// <summary>停止事件驱动的局域网发现。停止属于正常取消。</summary>
        public void StopDiscovery()
        {
            Operation round; lock (_clientGate) round = _discovery;
            round?.Cancel();
        }

        /// <summary>主动断开当前连接，同时终止旧端点的持续恢复。</summary>
        public async Task<ConnectionResult> DisconnectAsync()
        {
            using var operation = EnterOperation();
            if (operation == null) return ShuttingDown();
            long intentVersion = BeginDisconnectIntent();
            bool acquired = false;
            try
            {
                await _connectionCoordinator.OperationGate.WaitAsync(operation.Token);
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
            using var operation = EnterOperation();
            if (operation == null) return ShuttingDown();
            bool acquired = false;
            try
            {
                await _connectionCoordinator.OperationGate.WaitAsync(operation.Token); acquired = true;
                PulletClient client = _client;
                return client == null
                    ? ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "PulletNet client has not started.")
                    : await client.SuspendAsync();
            }
            catch (Exception ex) { return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message); }
            finally { if (acquired) _connectionCoordinator.OperationGate.Release(); }
        }

        /// <summary>恢复此前挂起的底层网络会话。</summary>
        public async Task<ConnectionResult> ResumeAsync()
        {
            using var operation = EnterOperation();
            if (operation == null) return ShuttingDown();
            bool acquired = false;
            try
            {
                await _connectionCoordinator.OperationGate.WaitAsync(operation.Token); acquired = true;
                PulletClient client = _client;
                return client == null
                    ? ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "PulletNet client has not started.")
                    : await client.ResumeAsync();
            }
            catch (Exception ex) { return ConnectionResult.Fail(ConnectionErrorCode.InternalError, ex.Message); }
            finally { if (acquired) _connectionCoordinator.OperationGate.Release(); }
        }

        /// <summary>按标准投递模式发送原始 Payload；成功仅代表本地接纳，不代表远端接收或业务完成。</summary>
        public ValueTask<SendResult> SendAsync(
            byte[] payload,
            ChannelType channelType = ChannelType.ReliableOrdered,
            CancellationToken cancellationToken = default)
        {
            PulletClient client = _client;
            if (_isShuttingDown || client == null || client.State != ConnectionState.Connected)
                return new ValueTask<SendResult>(SendResult.Rejected(SendErrorCode.NotConnected, "PulletNet client is not connected."));
            // 3.0 SendAsync 的第二个参数是 ChannelId，不可将旧模式枚举强转为 ID。
            return client.SendDefaultAsync(payload, channelType, cancellationToken);
        }

        /// <summary>按当前协商的 ChannelId 发送；未协商的 ID 明确拒绝，不回退为其他通道。</summary>
        public ValueTask<SendResult> SendOnChannelAsync(
            byte[] payload,
            byte channelId,
            CancellationToken cancellationToken = default)
        {
            PulletClient client = _client;
            if (_isShuttingDown || client == null || client.State != ConnectionState.Connected)
                return new ValueTask<SendResult>(SendResult.Rejected(SendErrorCode.NotConnected, "PulletNet client is not connected."));
            return client.SendAsync(payload, channelId, cancellationToken);
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
            // Unity preserves numeric enum values in assets. Reject retired values
            // before constructing the SDK; never silently reinterpret old scenes.
            if ((byte)connectionMode == 2 || (byte)connectionMode == 3)
            {
                error = "Connection mode 2/3 is retired. Select UdpOnly, TcpOnly or WebSocketOnly explicitly; multiple transports require separate clients.";
                return false;
            }
            if (connectionMode != ConnectionMode.UdpOnly && connectionMode != ConnectionMode.TcpOnly &&
                connectionMode != ConnectionMode.WebSocketOnly)
            {
                error = "Unsupported connection mode. Select UdpOnly, TcpOnly or WebSocketOnly.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(targetHost))
            {
                error = "Host cannot be empty.";
                return false;
            }

            bool usesTcp = connectionMode == ConnectionMode.TcpOnly;
            bool usesUdp = connectionMode == ConnectionMode.UdpOnly;
            bool usesWebSocket = connectionMode == ConnectionMode.WebSocketOnly;
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
            if (payloadQueueCapacity <= 0 || maxPayloadCallbacksPerFrame <= 0 ||
                payloadQueueBytes <= 0 || maxPayloadBytesPerFrame <= 0)
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
                await ReleaseDisconnectedClientAsync();
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
                    finally { await RemoveDefaultClientAsync(); }
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
                // Keep a failed source until the next explicit attempt or policy
                // cleanup, so Update can deliver its already-recorded failure.
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
            if (Thread.CurrentThread.ManagedThreadId != _unityThread)
                throw new InvalidOperationException("Create the Manager's default client on its Unity thread.");
            lock (_clientGate)
            {
                if (_isShuttingDown) throw new ObjectDisposedException(nameof(PulletNetworkManager));
                bool sameEndpoint = _client != null && string.Equals(_activeHost, targetHost, StringComparison.OrdinalIgnoreCase) &&
                                    _activeTcpPort == targetTcpPort && _activeUdpPort == targetUdpPort &&
                                    _activeWebSocketPort == targetWebSocketPort;
                if (sameEndpoint) return _client;
                if (_client != null) throw new InvalidOperationException("Disconnect the current client before changing endpoint.");

                var options = CreateClientOptions(targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort);
                if (_runtime == null)
                {
                    _runtime = new PulletClientRuntime(new PulletClientRuntimeOptions
                    {
                        MaximumClients = 1, MaximumPayloads = payloadQueueCapacity,
                        PayloadsPerClient = payloadQueueCapacity, MaximumPayloadBytes = payloadQueueBytes,
                        PayloadBytesPerClient = payloadQueueBytes, PayloadCallbacksPerFrame = maxPayloadCallbacksPerFrame,
                        PayloadBytesPerFrame = maxPayloadBytesPerFrame
                    });
                    _runtime.LifecycleChanged += ForwardLifecycle;
                    _runtime.PayloadReceived += ForwardPayload;
                    _runtime.DeliveryFaulted += ForwardDeliveryFault;
                    _runtime.CallbackError += error => ReportCallbackError(error.Error);
                }
                _source = _runtime.Create("default", options);
                _activeHost = targetHost;
                _activeTcpPort = targetTcpPort;
                _activeUdpPort = targetUdpPort;
                _activeWebSocketPort = targetWebSocketPort;
                // Messaging is optional application state, not another network
                // lifecycle owner. Cancel pending calls at the actual SDK boundary,
                // before the deferred Unity event, without touching delivery queues.
                var source = _source;
                _sessionObserver = value =>
                {
                    if (!ReferenceEquals(source, _source)) return;
                    if (value.Current == ConnectionState.Handshaking || value.Current == ConnectionState.Disconnecting ||
                        value.Current == ConnectionState.Disconnected || value.Current == ConnectionState.Disposed)
                        _messages?.CancelPending("PulletNet session changed: " + value.Current);
                };
                source.Client.ConnectionStateChanged += _sessionObserver;
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

        private void ForwardLifecycle(PulletClientLifecycleEvent value)
        {
            if (_isShuttingDown || !ReferenceEquals(value.Source, _source)) return;
            switch (value.Kind)
            {
                case PulletClientLifecycleKind.Connected:
                    LogInfo("服务器连接成功。"); InvokeEach(Connected, (ConnectedEvent)value.Detail); break;
                case PulletClientLifecycleKind.ConnectFailed:
                    // Explicit/auto requests report their returned result exactly once.
                    break;
                case PulletClientLifecycleKind.Disconnected:
                    InvokeEach(Disconnected, (DisconnectedEvent)value.Detail); break;
                case PulletClientLifecycleKind.ReconnectAttempt:
                    InvokeEach(ReconnectAttempt, (ReconnectAttemptEvent)value.Detail); break;
                case PulletClientLifecycleKind.Reconnected:
                    InvokeEach(Reconnected); break;
                case PulletClientLifecycleKind.ReconnectFailed:
                    var failure = (ReconnectFailedEvent)value.Detail;
                    InvokeEach(ReconnectFailed, failure);
                    if (!_isShuttingDown && ReferenceEquals(value.Source, _source))
                        BeginPersistentActiveServerReconnect(failure.Reason);
                    break;
            }
        }

        private void ForwardPayload(PulletClientPayloadEvent value)
        {
            if (_isShuttingDown || !ReferenceEquals(value.Source, _source)) return;
            // The Messaging envelope still requires byte[]. Raw consumers borrow
            // Runtime's memory directly; they do not pay for an extra array/queue.
            var messages = _messages;
            if (messages != null)
            {
                try { messages.HandlePayload(value.Payload.Memory.ToArray(), out _); }
                catch (Exception error) { ReportCallbackError(error); }
            }
            foreach (var handler in _payloadHandlers.Snapshot)
            {
                if (_isShuttingDown || !ReferenceEquals(value.Source, _source) ||
                    !_runtime.IsCurrentPayload(value.Source, value.DispatchGeneration)) break;
                try { handler(value.Payload); } catch (Exception error) { ReportCallbackError(error); }
            }
        }

        private void ForwardDeliveryFault(PulletClientDeliveryFailure value)
        {
            if (_isShuttingDown || !ReferenceEquals(value.Source, _source)) return;
            if (value.Reason == PulletClientDeliveryFailureReason.ReliableOverflow)
                Interlocked.Increment(ref _reliableOverflows);
            try
            {
                _messages?.CancelPending("Unity delivery failed: " + value.Reason);
                InvokeEach(PayloadQueueFaulted, "Unity delivery failed: " + value.Reason);
            }
            finally
            {
                // This facade has only one source. Incomplete reliable delivery
                // must not silently reconnect and continue application state.
                // Request close while the terminal Runtime fact is still owned.
                ObserveRequest(ShutdownAsync());
            }
        }

        private void PublishDiscoveryStarted()
        {
            LogInfo($"服务器发现开始：UDP {discoveryPort}，最多广播 {discoveryMaxAttempts} 次，" +
                    $"间隔 {discoverySendIntervalSeconds:0.##} 秒，接收窗口 {discoveryTimeoutSeconds:0.##} 秒。");
            InvokeEach(DiscoveryStarted);
        }

        private void PublishDiscoveryAttempt(int attempt, int maximum)
        {
            if (DiscoveryAttempted == null) return;
            foreach (Action<int, int> handler in DiscoveryAttempted.GetInvocationList())
            { if (_isShuttingDown) break; try { handler(attempt, maximum); } catch (Exception error) { ReportCallbackError(error); } }
        }

        private void PublishDiscoverySuccess(IReadOnlyList<PulletServerInfo> servers)
        {
            PulletServerInfo[] snapshot = servers == null ? Array.Empty<PulletServerInfo>() : servers.ToArray();
            LogInfo($"服务器发现成功：共找到 {snapshot.Length} 个匹配服务器。");
            LastDiscoveredServers = snapshot;
            InvokeEach(ServersDiscovered, (IReadOnlyList<PulletServerInfo>)snapshot);
            InvokeEach(DiscoverySucceeded, (IReadOnlyList<PulletServerInfo>)snapshot);
        }

        private void PublishDiscoveryFailure(string error)
        {
            string message = string.IsNullOrWhiteSpace(error) ? "Server discovery failed." : error;
            LogWarning("服务器发现失败：" + message);
            InvokeEach(DiscoveryFailed, message);
        }

        private void PublishDiscoveryStopped()
        {
            LogInfo("服务器发现结束。");
            InvokeEach(DiscoveryStopped);
        }

        private void ReportConnectFailure(ConnectionResult result)
        {
            if (result.IsSuccess || _isShuttingDown) return;
            string message = result.Message ?? "PulletNet connection failed.";
            LogWarning("服务器连接失败：" + message);
            var value = new ConnectFailedEvent(message);
            InvokeEach(ConnectFailed, value);
        }

        private void LogInfo(string message)
        {
            if (!enableConnectionLogs || _isShuttingDown) return;
            try { Debug.Log("[PulletNet] " + message, this); } catch (Exception error) { ReportCallbackError(error); }
        }

        private void LogWarning(string message)
        {
            if (!enableConnectionLogs || _isShuttingDown) return;
            try { Debug.LogWarning("[PulletNet] " + message, this); } catch (Exception error) { ReportCallbackError(error); }
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

        // Only application/discovery facts use this bounded queue. SDK lifecycle
        // and payloads go straight through Runtime's one delivery pipeline.
        private void Enqueue(Action action)
        {
            bool overflow = false;
            lock (_clientGate)
            {
                if (_isShuttingDown) return;
                if (_applicationEvents.Count + _applicationCallbacks >= MaximumApplicationEvents)
                {
                    _applicationQueueFailure = _applicationQueueFailure ?? new InvalidOperationException("Manager application event capacity reached.");
                    overflow = true;
                }
                else _applicationEvents.Enqueue(action);
            }
            // A full queue cannot hide its own terminal condition in that queue.
            if (overflow) ObserveRequest(ShutdownAsync());
        }

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
        private PulletConnectionCoordinator.ConnectionIntent BeginConnectionIntent(CancellationToken requestToken = default)
        {
            _activeServerRecovery?.Cancel();
            return _connectionCoordinator.BeginConnection(_lifetimeCts.Token, requestToken);
        }

        private long BeginDisconnectIntent()
        {
            _activeServerRecovery?.Cancel();
            return _connectionCoordinator.BeginDisconnect();
        }

        private void CompleteConnectionIntent(PulletConnectionCoordinator.ConnectionIntent intent)
            => _connectionCoordinator.Complete(intent);

        private bool IsCurrentIntent(long version)
            => !_isShuttingDown && _connectionCoordinator.IsCurrent(version);

        private void InvalidateSession(string reason)
        {
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

        private void OnApplicationPause(bool pauseStatus) => SetApplicationPauseReason(1, pauseStatus);
        private void OnApplicationFocus(bool hasFocus) => SetApplicationPauseReason(2, !hasFocus);

        private void SetApplicationPauseReason(int reason, bool paused)
        {
            lock (_clientGate)
            {
                if (_isShuttingDown || !reconnectOnApplicationResume) return;
                bool wasPaused = _applicationPauseReasons != 0;
                _applicationPauseReasons = paused ? _applicationPauseReasons | reason : _applicationPauseReasons & ~reason;
                if (wasPaused == (_applicationPauseReasons != 0)) return;
            }
            ObserveRequest(ApplyPauseAsync());
        }

        private async Task ApplyPauseAsync()
        {
            using var operation = EnterOperation();
            if (operation == null) return;
            if (_isShuttingDown || !reconnectOnApplicationResume) return;
            bool acquired = false;
            try
            {
                await _connectionCoordinator.OperationGate.WaitAsync(operation.Token); acquired = true;
                bool paused; lock (_clientGate) paused = _applicationPauseReasons != 0;
                var client = _client;
                if (client == null) return;
                // Evaluate the latest combined application intent after entering
                // the same endpoint-operation gate. Pause/focus are not competing
                // SDK state machines and cannot resume before suspend exits.
                if (paused)
                {
                    _resumeShouldReconnect |= IsConnected;
                    if (IsConnected) await client.SuspendAsync();
                }
                else if (_resumeShouldReconnect)
                {
                    _resumeShouldReconnect = false;
                    ConnectionResult result = await client.ResumeAsync();
                    if (!result.IsSuccess) Enqueue(() => LogWarning(result.Message ?? "PulletNet resume failed."));
                }
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
            catch (Exception ex) { Enqueue(() => LogWarning(ex.Message)); }
            finally { if (acquired) _connectionCoordinator.OperationGate.Release(); }
        }

        /// <summary>
        /// Irreversibly close this facade. Joins accepted application operations,
        /// recovery, discovery and Runtime (including entered callbacks). Call and
        /// await before destroying the object when an exit receipt is required.
        /// Never synchronously wait from a Unity/network callback. A caller token
        /// cancels only that caller's wait, not shared cleanup.
        /// </summary>
        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            TaskCompletionSource<bool> owner = null; Task operations = null;
            lock (_clientGate)
            {
                if (_shutdown == null)
                {
                    _isShuttingDown = true;
                    owner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _shutdown = owner.Task; operations = _operationsExited.Task;
                    _applicationEvents.Clear();
                }
            }
            if (owner != null) _ = ShutdownCoreAsync(owner, operations);
            return cancellationToken.CanBeCanceled ? WaitForCaller(_shutdown, cancellationToken) : _shutdown;
        }

        private async Task ShutdownCoreAsync(TaskCompletionSource<bool> owner, Task operations)
        {
            var failures = new List<Exception>();
            Task recovery = Task.CompletedTask;
            Task<PulletClientRuntimeCloseResult> runtime = null;
            // Start every stop request before joining any owner. A blocked connect
            // may require Runtime close to unblock; holding OperationGate here
            // would deadlock that accepted operation.
            try { recovery = _activeServerRecovery?.StopAsync() ?? Task.CompletedTask; } catch (Exception e) { failures.Add(e); }
            try { _lifetimeCts?.Cancel(); } catch (Exception e) { failures.Add(e); }
            try { runtime = _runtime?.CloseAsync(); } catch (Exception e) { failures.Add(e); }
            try { await recovery; } catch (Exception e) { failures.Add(e); }
            try { await operations; } catch (Exception e) { failures.Add(e); }
            try
            {
                if (runtime != null)
                {
                    var result = await runtime;
                    if (result.HostCleanupError != null) failures.Add(result.HostCleanupError);
                    foreach (var client in result.Clients)
                    {
                        if (client.CleanupError != null) failures.Add(client.CleanupError);
                        if (client.DeliveryFailure != null) failures.Add(client.DeliveryFailure.Error ??
                            new InvalidOperationException("Unity delivery failed: " + client.DeliveryFailure.Reason));
                    }
                }
            }
            catch (Exception e) { failures.Add(e); }
            try
            {
                if (_source != null && _sessionObserver != null) _source.Client.ConnectionStateChanged -= _sessionObserver;
                _source = null; _sessionObserver = null;
            }
            catch (Exception e) { failures.Add(e); }
            try
            {
                var messages = _messages; _messages = null;
                if (messages != null)
                {
                    messages.UnhandledMessage -= ForwardUnhandledBusinessMessage;
                    messages.ProtocolError -= ForwardMessageProtocolError;
                    messages.Dispose();
                }
            }
            catch (Exception e) { failures.Add(e); }
            try { _lifetimeCts?.Dispose(); } catch (Exception e) { failures.Add(e); }
            if (_applicationQueueFailure != null) failures.Insert(0, _applicationQueueFailure);
            if (failures.Count == 0) owner.TrySetResult(true);
            else owner.TrySetException(new AggregateException("PulletNetworkManager shutdown failed.", failures));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_shutdown == null) ObserveRequest(ShutdownAsync());
        }

        private void OnApplicationQuit() { if (_shutdown == null) ObserveRequest(ShutdownAsync()); }

        private Task ReleaseDisconnectedClientAsync()
        {
            var client = _client;
            return client != null && (client.State == ConnectionState.Disconnected || client.State == ConnectionState.Disposed)
                ? RemoveDefaultClientAsync() : Task.CompletedTask;
        }

        private async Task RemoveDefaultClientAsync()
        {
            HostedClient source; Action<ConnectionStateChangedEvent> observer;
            Task<PulletClientCloseResult> removal;
            lock (_clientGate)
            {
                source = _source;
                if (source == null || _isShuttingDown) return; // shared Shutdown owns the same source now
                observer = _sessionObserver;
                removal = _runtime.RemoveAsync(source);
                _source = null; _sessionObserver = null;
                _activeHost = null;
                _activeTcpPort = _activeUdpPort = _activeWebSocketPort = 0;
            }
            if (observer != null) source.Client.ConnectionStateChanged -= observer;
            InvalidateSession("PulletNet client was replaced or disposed.");
            var result = await removal;
            if (!result.IsSuccess) throw result.CleanupError ?? result.DeliveryFailure?.Error ??
                new InvalidOperationException("Default source was retired after delivery failure.");
        }

        // Application operation accounting, not an SDK connection state machine.
        // Published under the same admission lock as Shutdown so no accepted
        // strategy/discovery/pause operation can disappear from the final join.
        private Operation EnterOperation(CancellationToken token = default)
        {
            lock (_clientGate)
            {
                if (_isShuttingDown || _lifetimeCts == null) return null;
                if (_operations >= MaximumOperations) throw new InvalidOperationException("Manager application operation capacity reached.");
                var operation = new Operation(this, CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, token));
                BeginOperationCount(); return operation;
            }
        }

        private void BeginOperationCount()
        {
            if (_operations++ == 0) _operationsExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void EndOperationCount()
        {
            if (--_operations == 0) _operationsExited.TrySetResult(true);
        }

        private sealed class Operation : IDisposable
        {
            private readonly object _gate = new object();
            private readonly PulletNetworkManager _owner;
            private readonly CancellationTokenSource _cancellation;
            private bool _disposed;
            internal CancellationToken Token { get; }
            internal Operation(PulletNetworkManager owner, CancellationTokenSource cancellation)
            { _owner = owner; _cancellation = cancellation; Token = cancellation.Token; }
            internal void Cancel() { lock (_gate) if (!_disposed) _cancellation.Cancel(); }
            public void Dispose()
            {
                lock (_gate) { if (_disposed) return; _disposed = true; _cancellation.Dispose(); }
                lock (_owner._clientGate) _owner.EndOperationCount();
            }
        }

        private static TaskCompletionSource<bool> CompletedSignal()
        { var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); signal.SetResult(true); return signal; }

        private static ConnectionResult ShuttingDown()
            => ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is not initialized or is shutting down.");

        private static async Task WaitForCaller(Task shared, CancellationToken token)
        {
            if (!token.CanBeCanceled) { await shared; return; }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                var completed = await Task.WhenAny(shared, canceled.Task);
                await completed;
            }
        }

        private static void ObserveRequest(Task request)
        {
            // Diagnostic continuation owns no resources or additional operation.
            _ = request.ContinueWith(task => ReportCallbackError(task.Exception), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static void ReportCallbackError(Exception error)
        { try { Debug.LogException(error); } catch { /* diagnostics must not interrupt owner settlement */ } }

        private void InvokeEach(Action handlers)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            { if (_isShuttingDown) break; try { handler(); } catch (Exception error) { ReportCallbackError(error); } }
        }

        private void InvokeEach<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            { if (_isShuttingDown) break; try { handler(value); } catch (Exception error) { ReportCallbackError(error); } }
        }

        private static bool IsValidPort(int port) => port > 0 && port <= 65535;
    }
}
