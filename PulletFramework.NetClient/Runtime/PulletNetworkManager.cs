using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;
using PulletClient = PulletNet.ClientSDK.NetClient;
using UnityEngine;
using UnityEngine.Serialization;

namespace PulletFramework.NetClient
{
    /// <summary>
    /// Unity单默认连接门面，负责组件生命周期和应用连接意图；底层客户端由Runtime/SDK创建并释放。
    /// 不强制应用使用本组件；有自有登录、消息来源或多连接流程时可直接使用PulletClientRuntime。
    /// <para>Optional Unity facade for one default connection and application intent.
    /// Runtime/SDK owns client creation and final release. Applications with custom login,
    /// message-source or multi-connection flows may use PulletClientRuntime directly.</para>
    /// </summary>
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
        [Tooltip("组件启动时只连接固定端点。发现策略由可选 PulletDiscoveryConnector 管理，不可同时启用两个启动入口。")]
        public bool connectOnStart;
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

        // Serialized migration guard, not a second discovery implementation.
        // Old assets must explicitly move their policy/settings/events before they can connect.
        [SerializeField, HideInInspector, FormerlySerializedAs("autoConnectMode")]
        private int _legacyDiscoveryMode = -1;
        public bool RequiresDiscoveryMigration => _legacyDiscoveryMode >= 0;

        [ContextMenu("Confirm legacy Discovery settings and events have been migrated")]
        public void ConfirmDiscoveryMigration()
        {
            // Explicit user acknowledgement; this never guesses a replacement policy.
            _legacyDiscoveryMode = -1;
        }

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
        private const int MaximumOperations = 32, MaximumApplicationEvents = 128, ApplicationEventsPerFrame = 64;
        private readonly Queue<Action> _applicationEvents = new Queue<Action>();
        private TaskCompletionSource<bool> _operationsExited = CompletedSignal();
        private int _operations, _applicationCallbacks;
        private Task _shutdown;
        private Exception _applicationQueueFailure;
        private long _reliableOverflows;
        private CancellationTokenSource _lifetimeCts;
        private bool _resumeShouldReconnect;
        private int _applicationPauseReasons;
        private volatile bool _isShuttingDown;
        private PulletActiveServerRecovery _activeServerRecovery;
        // Borrowed client's configuration, distinct from the last successful endpoint.
        private ConnectionEndpoint _clientEndpoint;

        /// <summary>底层传输已完成握手，可安全发送应用消息。</summary>
        /// <summary>Local application command revision; not a SessionId or wire identity.</summary>
        public long ConnectionRevision => _connectionCoordinator.Version;
        public bool IsShuttingDown => _isShuttingDown;
        public bool IsConnected => _client != null && _client.State == ConnectionState.Connected;
        /// <summary>最近一次成功连接的不可变端点；失败尝试、断开或 Inspector 修改不会覆盖它。</summary>
        public ConnectionEndpoint ActiveEndpoint { get; private set; }
        public long DroppedPayloadCount => _runtime?.GetSnapshot().DroppedPayloads ?? 0;
        public long ReliablePayloadOverflowCount => Interlocked.Read(ref _reliableOverflows);
        /// <summary>是否正在持续恢复最后一次选择的服务器。</summary>
        public bool IsRecoveringActiveServer => _activeServerRecovery != null && _activeServerRecovery.IsRunning;

        /// <summary>首次连接成功。重连成功使用 <see cref="Reconnected"/>。</summary>
        public event Action<ConnectedEvent> Connected;
        /// <summary>一次显式连接请求失败。</summary>
        public event Action<ConnectFailedEvent> ConnectFailed;
        /// <summary>现有连接已断开。</summary>
        public event Action<DisconnectedEvent> Disconnected;
        /// <summary>原始应用 Payload；可选强类型协议由独立 Messaging 组合负责。</summary>
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
                (target, token) => ConnectEndpointAsync(target, token, _connectionCoordinator.Version),
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
                ObserveRequest(ConnectAndReportAsync());
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
            return await ConnectManualAsync(new ConnectionEndpoint(host, tcpPort, udpPort, webSocketPort),
                $"手动连接固定服务器：{FormatEndpoint(host, tcpPort, udpPort)}");
        }

        /// <summary>使用当前端口配置连接指定主机。</summary>
        public async Task<ConnectionResult> ConnectAsync(string targetHost)
        {
            return await ConnectManualAsync(new ConnectionEndpoint(targetHost, tcpPort, udpPort, webSocketPort),
                $"手动连接指定服务器：{FormatEndpoint(targetHost, tcpPort, udpPort)}");
        }

        /// <summary>
        /// Connect an exact endpoint using this Manager's transport/path/TLS options.
        /// No port fallback is performed. Cancellation cancels this accepted intent;
        /// the returned result follows real SDK cleanup, not only a cancellation request.
        /// A pre-canceled request does not supersede another connection or recovery.
        /// </summary>
        public Task<ConnectionResult> ConnectAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            return ConnectManualAsync(endpoint, $"连接端点：{endpoint}", cancellationToken);
        }

        private async Task<ConnectionResult> ConnectManualAsync(
            ConnectionEndpoint endpoint, string description, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ConnectionResult.Fail(ConnectionErrorCode.Canceled, "PulletNet connection was cancelled.");
            using var operation = EnterOperation(cancellationToken);
            if (operation == null) return ShuttingDown();
            if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed, "PulletNet client is shutting down.");
            if (IsConnected)
                return ConnectionResult.Fail(ConnectionErrorCode.InvalidState,
                    "Disconnect the current server before starting another connection.");
            PulletConnectionCoordinator.ConnectionIntent intent;
            try { intent = BeginConnectionIntent(operation.Token); }
            catch (ObjectDisposedException)
            {
                return ConnectionResult.Fail(ConnectionErrorCode.Disposed,
                    "PulletNet client is shutting down.");
            }
            try
            {
                LogInfo(description);
                ConnectionResult result = await ConnectEndpointAsync(endpoint, intent.Token, intent.Version);
                if (!result.IsSuccess && IsCurrentIntent(intent.Version) && !intent.Token.IsCancellationRequested)
                    Enqueue(() => { if (IsCurrentIntent(intent.Version)) ReportConnectFailure(result); });
                return result;
            }
            finally { CompleteConnectionIntent(intent); }
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
            if (RequiresDiscoveryMigration)
            {
                error = "Legacy Discovery configuration requires explicit migration to PulletDiscoveryConnector. Back up old fields/events before upgrading; confirm migration only after transferring them.";
                return false;
            }
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
            ConnectionEndpoint endpoint, CancellationToken cancellationToken, long intentVersion)
        {
            string targetHost = endpoint.Host;
            int targetTcpPort = endpoint.TcpPort, targetUdpPort = endpoint.UdpPort,
                targetWebSocketPort = endpoint.WebSocketPort;
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
                    ActiveEndpoint = endpoint;
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
                bool sameEndpoint = _client != null && _clientEndpoint != null &&
                                    string.Equals(_clientEndpoint.Host, targetHost, StringComparison.OrdinalIgnoreCase) &&
                                    _clientEndpoint.TcpPort == targetTcpPort && _clientEndpoint.UdpPort == targetUdpPort &&
                                    _clientEndpoint.WebSocketPort == targetWebSocketPort;
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
                _clientEndpoint = new ConnectionEndpoint(targetHost, targetTcpPort, targetUdpPort, targetWebSocketPort);
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

        // Only application control facts use this bounded queue. SDK lifecycle
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

        /// <summary>明确连接/断开会取代旧应用连接意图；不解释业务消息。</summary>
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

        private void BeginPersistentActiveServerReconnect(string reason)
        {
            if (!keepReconnectingActiveServer || _isShuttingDown || _activeServerRecovery == null ||
                _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return;

            ConnectionEndpoint target = ActiveEndpoint;
            if (target == null || string.IsNullOrWhiteSpace(target.Host))
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

        private async Task ConnectAndReportAsync()
        {
            ConnectionResult result = await ConnectAsync(new ConnectionEndpoint(host, tcpPort, udpPort, webSocketPort), _lifetimeCts.Token);
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
        /// recovery and Runtime (including entered callbacks). Call and
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
            _source = null;
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

        /// <summary>
        /// Retire a failed/disconnected default source before an application policy
        /// proceeds. Does not disconnect a live source. A newer command cancels this
        /// request at the gate; accepted removal is joined even if its waiter cancels.
        /// </summary>
        public async Task RetireDisconnectedClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var operation = EnterOperation(cancellationToken);
            if (operation == null) throw new ObjectDisposedException(nameof(PulletNetworkManager));
            long revision = ConnectionRevision;
            await _connectionCoordinator.OperationGate.WaitAsync(operation.Token);
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentIntent(revision)) throw new OperationCanceledException("Retirement was superseded.");
                if (_client != null && _client.State != ConnectionState.Disconnected && _client.State != ConnectionState.Disposed)
                    throw new InvalidOperationException("Only a disconnected source can be retired.");
                await ReleaseDisconnectedClientAsync();
            }
            finally { _connectionCoordinator.OperationGate.Release(); }
        }

        private Task ReleaseDisconnectedClientAsync()
        {
            var client = _client;
            return client != null && (client.State == ConnectionState.Disconnected || client.State == ConnectionState.Disposed)
                ? RemoveDefaultClientAsync() : Task.CompletedTask;
        }

        private async Task RemoveDefaultClientAsync()
        {
            HostedClient source;
            Task<PulletClientCloseResult> removal;
            lock (_clientGate)
            {
                source = _source;
                if (source == null || _isShuttingDown) return; // shared Shutdown owns the same source now
                removal = _runtime.RemoveAsync(source);
                _source = null;
                _clientEndpoint = null;
            }
            var result = await removal;
            if (!result.IsSuccess) throw result.CleanupError ?? result.DeliveryFailure?.Error ??
                new InvalidOperationException("Default source was retired after delivery failure.");
        }

        // Application operation accounting, not an SDK connection state machine.
        // Published under the same admission lock as Shutdown so no accepted
        // connect/pause operation can disappear from the final join.
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
