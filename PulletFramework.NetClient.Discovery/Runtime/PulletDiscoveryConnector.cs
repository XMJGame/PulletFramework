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
    /// Optional selection policy. Owns discovery and its workflow, never the borrowed
    /// Manager or its client. Close this component and then Shutdown the Manager.
    /// Construct/use/pump on the Unity thread; Close may be requested from any thread.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PulletNetworkManager))]
    public sealed class PulletDiscoveryConnector : MonoBehaviour
    {
        public AutoConnectMode autoConnectMode = AutoConnectMode.DirectThenDiscover;
        public bool connectOnStart;
        public bool enableDiscovery = true;
        public string discoveryAddress = "255.255.255.255";
        public int discoveryPort = 58888;
        [Min(.25f)] public float discoveryTimeoutSeconds = 3;
        [Range(1, 32)] public int discoveryMaxAttempts = 5;
        [Min(.1f)] public float discoverySendIntervalSeconds = .5f;
        [Min(.25f)] public float discoveryRetrySeconds = 1;
        public bool retryDiscoveryUntilConnected = true;
        public string discoveryServiceType = "pulletnet";
        [Range(1, 4096)] public int maximumResults = 256;
        [Range(1, 65536)] public int maximumReceivedDatagrams = 4096;
        public int maximumRetainedBytes = 1024 * 1024;
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

        /// <summary>One round; cancellation is observable, not an empty successful result. Pump Update before reusing.</summary>
        public Task<DiscoverySnapshot> DiscoverAsync(CancellationToken token = default)
        {
            RequireThread();
            if (!ValidateConfiguration(out var error)) throw new InvalidOperationException(error);
            if (!enableDiscovery) throw new InvalidOperationException("Discovery is disabled.");
            if (IsAutoConnecting) throw new InvalidOperationException("The automatic workflow owns discovery.");
            return _discovery.DiscoverAsync(Options(), token);
        }

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

        /// <summary>Permanent, shared join of this component only; does not own/close the Manager.</summary>
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
