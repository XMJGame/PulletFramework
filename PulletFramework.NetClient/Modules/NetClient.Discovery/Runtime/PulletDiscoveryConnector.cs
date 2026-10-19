using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.Discovery;
using PulletNet.ClientSDK;
using PulletNet.Discovery;
using UnityEngine;

namespace PulletFramework.NetClient.Discovery
{
    /// <summary>
    /// 可选的发现与连接选择策略：拥有发现任务及其流程，不拥有借用的Manager或客户端。
    /// 在Unity主线程创建、使用和推进；可从其它线程请求关闭。先等待本组件CloseAsync，再关闭Manager。
    /// <para>Optional selection policy. Owns discovery and its workflow, never the borrowed
    /// Manager or its client. Construct/use/pump on the Unity thread; Close may be requested
    /// from any thread. Await this component's close before shutting down the Manager.</para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PulletNetworkManager))]
    public sealed class PulletDiscoveryConnector : MonoBehaviour
    {
        [Tooltip("直接连接：只连Manager固定端点；先发现再连接：跳过固定端点；先直连失败后发现：固定连接失败并清理后搜索。关闭发现时三种模式都只走固定连接。")]
        public AutoConnectMode autoConnectMode = AutoConnectMode.DirectThenDiscover;
        [Tooltip("Unity Start时自动调用一次AutoConnectAsync，执行所选策略。关闭后由按钮/代码显式调用；它不是组件启用开关，也不是持续重试开关。Manager.connectOnStart必须关闭，避免两个启动入口。")]
        public bool connectOnStart;
        [Tooltip("允许策略使用局域网发现。关闭时AutoConnectAsync只连接Manager固定端点；单独DiscoverAsync会拒绝执行。不会清除已保存的发现配置。")]
        public bool enableDiscovery = true;
        [Tooltip("IPv4发现目标，默认255.255.255.255为有限广播。指定子网或已知主机时可改成子网广播/单播地址；不保证跨路由器或自动选择所有网卡。")]
        public string discoveryAddress = "255.255.255.255";
        [Tooltip("发现UDP端口，必须与服务端发现响应器一致；不是游戏连接端口。范围1–65535。")]
        public int discoveryPort = 58888;
        [Min(.25f), Tooltip("一轮发现的最长收集时间（秒），到期返回已收集候选；不是连接超时。必须大于0且不超过60秒。")]
        public float discoveryTimeoutSeconds = 3;
        [Range(1, 32), Tooltip("一轮发现最多发送请求次数（1–32）。仍受本轮截止时间限制，不代表固定端点连接重试次数。")]
        public int discoveryMaxAttempts = 5;
        [Min(.1f), Tooltip("一轮内两次发现请求的发送间隔（秒），必须大于0且不超过60秒；过短会增加广播与应答压力。")]
        public float discoverySendIntervalSeconds = .5f;
        [Min(.25f), Tooltip("启用持续发现重试后，两轮未成功连接的发现流程之间等待的秒数。必须大于0且不超过60秒；不控制已连接后的重连。")]
        public float discoveryRetrySeconds = 1;
        [Tooltip("自动连接未成功时继续新一轮发现，直到连接成功、取消、关闭或连接意图被替代。只对发现流程生效，不让Direct模式循环重试，也不接管断线重连。")]
        public bool retryDiscoveryUntilConnected = true;
        [Tooltip("发现服务类型筛选，应与服务端广告一致，例如pulletnet-xrvehicle；候选匹配不是业务鉴权。")]
        public string discoveryServiceType = "pulletnet";
        [Range(1, 4096), Tooltip("一轮最多保留的候选服务器数量（1–4096），达到上限提前结束；不是客户端或玩家数量上限。")]
        public int maximumResults = 256;
        [Range(1, 65536), Tooltip("一轮最多处理的接收数据报数量（1–65536），限制无效/重复流量消耗，达到上限提前结束；不是发送次数。")]
        public int maximumReceivedDatagrams = 4096;
        [Tooltip("一轮保留候选字符串/键的UTF-16字节预算（1字节–64MiB），达到上限提前结束。不等于进程内存、Socket缓存或Payload队列预算。")]
        public int maximumRetainedBytes = 1024 * 1024;
        [Tooltip("输出自动连接模式、发现轮次和连接结果等中文诊断；关闭后不输出本组件日志，不影响运行策略。")]
        public bool enableLogs = true;

        private readonly object _gate = new object();
        private DiscoveryRuntime _discovery;
        private PulletNetworkManager _manager;
        private Workflow _workflow;
        private Task<ConnectionResult> _lastWorkflow = Task.FromResult(ConnectionResult.Ok());
        private Task _close;
        private long _expectedRevision, _callbackFailures;
        private int _thread;
        private bool _initialized;
        public bool IsDiscovering => _discovery != null && _discovery.IsDiscovering;
        public bool IsAutoConnecting { get { lock (_gate) return _workflow != null; } }
        public long CallbackFailures => Interlocked.Read(ref _callbackFailures);
        public IReadOnlyList<DiscoveredServer> LastDiscoveredServers { get; private set; } = Array.Empty<DiscoveredServer>();
        public event Action DiscoveryStarted;
        public event Action<int, int> DiscoveryAttempted;
        public event Action<IReadOnlyList<DiscoveredServer>> DiscoverySucceeded;
        public event Action<string> DiscoveryFailed;
        public event Action DiscoveryStopped;
        public event Action<Exception> CallbackError;

        private bool Closing { get { lock (_gate) return _close != null; } }
        private void Awake() => Initialize();
        private void Initialize()
        {
            lock (_gate)
            {
            if (_initialized) return;
            if (_close != null) throw new ObjectDisposedException(nameof(PulletDiscoveryConnector));
            _thread = Thread.CurrentThread.ManagedThreadId;
            _manager = GetComponent<PulletNetworkManager>();
            _discovery = new DiscoveryRuntime();
            _discovery.Started += _ => InvokeEach(DiscoveryStarted);
            _discovery.Attempted += (_, attempt, maximum) => InvokeEach(DiscoveryAttempted, attempt, maximum);
            _discovery.Completed += OnDiscoveryCompleted;
            _discovery.CallbackError += ReportCallbackError;
            _initialized = true;
            }
        }

        private void Start()
        {
            if (connectOnStart) Observe(AutoConnectAsync());
        }
        private void Update()
        {
            if (!_initialized || Closing) return;
            Workflow run; lock (_gate) run = _workflow;
            if (run != null && (_manager.IsShuttingDown || _manager.ConnectionRevision != _expectedRevision))
                RequestCancel(run);
            _discovery.Update();
        }

        public bool ValidateConfiguration(out string error)
        {
            error = null;
            if (!Enum.IsDefined(typeof(AutoConnectMode), autoConnectMode)) error = "Unknown AutoConnectMode; values 0/1/2 retain their original meaning.";
            else if (GetComponent<PulletNetworkManager>().connectOnStart) error = "Disable Manager.connectOnStart: select exactly one startup connection owner.";
            else if (enableDiscovery &&
                (!IPAddress.TryParse(discoveryAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
                 discoveryPort < 1 || discoveryPort > 65535 ||
                 !Finite(discoveryTimeoutSeconds, 0, 60) || !Finite(discoverySendIntervalSeconds, 0, 60) ||
                 discoveryMaxAttempts < 1 || discoveryMaxAttempts > 32 ||
                 !Finite(discoveryRetrySeconds, 0, 60) ||
                 maximumResults < 1 || maximumResults > 4096 ||
                 maximumReceivedDatagrams < 1 || maximumReceivedDatagrams > 65536 ||
                 maximumRetainedBytes < 1 || maximumRetainedBytes > 64 * 1024 * 1024))
                error = "Discovery destination, deadlines, attempts or resource limits are invalid.";
            return error == null;
        }
        private static bool Finite(float value, float exclusiveMinimum, float maximum)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value > exclusiveMinimum && value <= maximum;
        private void RequireThread()
        {
            Initialize();
            if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Discovery selection belongs to its Unity thread.");
            if (Closing) throw new ObjectDisposedException(nameof(PulletDiscoveryConnector));
        }
        private DiscoveryOptions Options() => new DiscoveryOptions
        {
            Destination = new IPEndPoint(IPAddress.Parse(discoveryAddress), discoveryPort),
            Timeout = TimeSpan.FromSeconds(discoveryTimeoutSeconds), MaximumAttempts = discoveryMaxAttempts,
            SendInterval = TimeSpan.FromSeconds(discoverySendIntervalSeconds), ServiceType = discoveryServiceType,
            MaximumResults = maximumResults, MaximumReceivedDatagrams = maximumReceivedDatagrams,
            MaximumRetainedBytes = maximumRetainedBytes
        };

        /// <summary>
        /// 执行一轮发现；取消保持可观察，不伪装成成功空结果。消费Update中的终态后才能复用。
        /// <para>Run one discovery round. Cancellation is observable, not an empty success;
        /// pump Update to consume terminal delivery before reuse.</para>
        /// </summary>
        public Task<DiscoverySnapshot> DiscoverAsync(CancellationToken token = default)
        {
            RequireThread();
            if (!ValidateConfiguration(out var error)) throw new InvalidOperationException(error);
            if (!enableDiscovery) throw new InvalidOperationException("Discovery is disabled.");
            if (IsAutoConnecting) throw new InvalidOperationException("The automatic workflow owns discovery.");
            return _discovery.DiscoverAsync(Options(), token);
        }

        /// <summary>
        /// 在Unity主线程执行所选自动连接策略；关闭connectOnStart不影响显式调用。
        /// <para>Run the selected connection policy on the Unity thread;
        /// explicit calls are allowed even when connectOnStart is disabled.</para>
        /// </summary>
        public Task<ConnectionResult> AutoConnectAsync(CancellationToken token = default)
        {
            RequireThread();
            if (token.IsCancellationRequested) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Automatic connection canceled."));
            if (!ValidateConfiguration(out var error)) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.InvalidState, error));
            if (_manager.RequiresDiscoveryMigration) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "Confirm legacy Discovery migration before connecting."));
            if (_manager.IsShuttingDown) return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.Disposed, "Manager is shutting down."));
            if (_manager.IsConnected) return Task.FromResult(ConnectionResult.Ok());
            Workflow run;
            lock (_gate)
            {
                if (_close != null) throw new ObjectDisposedException(nameof(PulletDiscoveryConnector));
                if (_workflow != null || _discovery.IsDiscovering || _discovery.PendingNotices != 0)
                    return Task.FromResult(ConnectionResult.Fail(ConnectionErrorCode.InvalidState, "A discovery/workflow or its terminal delivery is still owned."));
                run = new Workflow(token);
                _workflow = run; _lastWorkflow = run.Exited.Task;
                _expectedRevision = _manager.ConnectionRevision;
            }
            // Publish before any synchronous delegate can finish or reenter.
            _ = RunAsync(run);
            return run.Exited.Task;
        }

        private async Task RunAsync(Workflow run)
        {
            ConnectionResult result = default; Exception failure = null;
            try
            {
                var options = new PulletAutoConnectStrategy.Options
                {
                    Mode = autoConnectMode, EnableDiscovery = enableDiscovery,
                    RetryDiscoveryUntilConnected = retryDiscoveryUntilConnected,
                    DiscoveryRetrySeconds = discoveryRetrySeconds,
                    FixedEndpointDescription = _manager.host,
                    DiscoveryDescription = discoveryAddress + ":" + discoveryPort
                };
                var discoveryOptions = OptionsIfEnabled();
                var fixedEndpoint = new ConnectionEndpoint(_manager.host, _manager.tcpPort, _manager.udpPort, _manager.webSocketPort);
                var strategy = new PulletAutoConnectStrategy(
                    token => ConnectSelected(run, fixedEndpoint),
                    async token => { CheckIntent(run); var snapshot = await _discovery.DiscoverAsync(discoveryOptions, token); CheckIntent(run); return snapshot.Servers; },
                    (server, token) => ConnectSelected(run, new ConnectionEndpoint(server.Host, server.TcpPort, server.UdpPort, server.WebPort)),
                    async () => { CheckIntent(run); await _manager.RetireDisconnectedClientAsync(run.Token); CheckIntent(run); },
                    () => !Closing && !_manager.IsShuttingDown, Log, Log);
                result = await strategy.RunAsync(options, run.Token);
            }
            catch (OperationCanceledException) when (run.Token.IsCancellationRequested || Closing ||
                _manager.IsShuttingDown || _manager.ConnectionRevision != _expectedRevision)
            { result = ConnectionResult.Fail(ConnectionErrorCode.Canceled, "Automatic selection canceled or superseded."); }
            catch (Exception error) { failure = error; }
            finally
            {
                run.DisposeCancellation();
                lock (_gate)
                {
                    _workflow = null;
                    if (failure != null) run.Exited.TrySetException(failure); else run.Exited.TrySetResult(result);
                }
            }
        }
        private DiscoveryOptions OptionsIfEnabled() => enableDiscovery ? Options() : null;
        private async Task<ConnectionResult> ConnectSelected(Workflow run, ConnectionEndpoint endpoint)
        {
            CheckIntent(run);
            var connect = _manager.ConnectAsync(endpoint, run.Token);
            _expectedRevision = _manager.ConnectionRevision; // this command, before yielding
            var result = await connect;
            CheckIntent(run);
            return result;
        }
        private void CheckIntent(Workflow run)
        {
            run.Token.ThrowIfCancellationRequested();
            if (Closing || _manager.IsShuttingDown || _manager.ConnectionRevision != _expectedRevision)
                throw new OperationCanceledException("A newer Manager command superseded automatic selection.");
        }

        private void OnDiscoveryCompleted(DiscoveryCompletion completion)
        {
            try
            {
                if (completion.Result != null)
                {
                    LastDiscoveredServers = completion.Result.Servers;
                    if (LastDiscoveredServers.Count != 0) InvokeEach(DiscoverySucceeded, LastDiscoveredServers);
                    else InvokeEach(DiscoveryFailed, "No matching server was discovered.");
                }
                else if (!completion.IsCanceled) InvokeEach(DiscoveryFailed, completion.Error?.Message ?? "Discovery failed.");
            }
            finally { InvokeEach(DiscoveryStopped); }
        }

        /// <summary>Cancel/join current policy and IO; leaves historical UI notices for the next enabled Update.</summary>
        public async Task StopAsync()
        {
            Workflow run; Task work;
            lock (_gate) { run = _workflow; work = _lastWorkflow; }
            Exception failure = null;
            try { RequestCancel(run); } catch (Exception error) { failure = error; }
            try { if (_discovery != null) await _discovery.StopAsync(); } catch (Exception error) { failure = Combine(failure, error); }
            try { await work; } catch (Exception error) { failure = Combine(failure, error); }
            if (failure != null) throw failure;
        }

        /// <summary>
        /// 永久关闭本组件接纳并等待其真实退出；共享关闭任务不拥有或关闭Manager。
        /// <para>Permanently close admission and join this component's work;
        /// the shared close does not own or close the Manager.</para>
        /// </summary>
        public Task CloseAsync()
        {
            TaskCompletionSource<bool> owner;
            lock (_gate)
            {
                if (_close != null) return _close;
                owner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = owner.Task;
            }
            _ = CloseCoreAsync(owner);
            return owner.Task;
        }
        private async Task CloseCoreAsync(TaskCompletionSource<bool> owner)
        {
            Exception failure = null; Workflow run; Task work;
            lock (_gate) { run = _workflow; work = _lastWorkflow; }
            try { RequestCancel(run); } catch (Exception error) { failure = error; }
            try { if (_discovery != null) await _discovery.CloseAsync(); } catch (Exception error) { failure = Combine(failure, error); }
            try { await work; } catch (Exception error) { failure = Combine(failure, error); }
            if (failure == null) owner.TrySetResult(true); else owner.TrySetException(failure);
        }
        private static void RequestCancel(Workflow run)
        { run?.Cancel(); }
        private static Exception Combine(Exception first, Exception next) => first == null ? next : new AggregateException(first, next);
        private void OnDisable() { if (_initialized) Observe(StopAsync()); }
        private void OnDestroy() => Observe(CloseAsync());
        private void OnApplicationQuit() => Observe(CloseAsync());

        private void Log(string message) { if (enableLogs && !Closing) Debug.Log("[PulletDiscovery] " + message, this); }
        private static void Observe(Task task) => _ = task.ContinueWith(t => { try { Debug.LogException(t.Exception); } catch { } },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        private void ReportCallbackError(Exception error)
        {
            Interlocked.Increment(ref _callbackFailures);
            if (CallbackError == null) return;
            foreach (Action<Exception> handler in CallbackError.GetInvocationList()) try { handler(error); } catch { Interlocked.Increment(ref _callbackFailures); }
        }
        private void InvokeEach(Action handlers) => Dispatch(handlers, h => ((Action)h)());
        private void InvokeEach<T>(Action<T> handlers, T value) => Dispatch(handlers, h => ((Action<T>)h)(value));
        private void InvokeEach(Action<int, int> handlers, int a, int b) => Dispatch(handlers, h => ((Action<int, int>)h)(a, b));
        private void Dispatch(Delegate handlers, Action<Delegate> invoke)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                if (Closing) return;
                try { invoke(handler); } catch (Exception error) { ReportCallbackError(error); }
            }
        }
        private sealed class Workflow
        {
            private readonly object _cancellationGate = new object();
            private bool _disposed;
            private readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken Token;
            internal readonly TaskCompletionSource<ConnectionResult> Exited = new TaskCompletionSource<ConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal void Cancel() { lock (_cancellationGate) if (!_disposed) Cancellation.Cancel(); }
            internal void DisposeCancellation() { lock (_cancellationGate) { if (_disposed) return; _disposed = true; Cancellation.Dispose(); } }
            internal Workflow(CancellationToken token) { Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); Token = Cancellation.Token; }
        }
    }
}
