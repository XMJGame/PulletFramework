using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;
using PulletClient = PulletNet.ClientSDK.NetClient;
using UnityEngine;
using UnityEngine.Events;

namespace PulletFramework.NetClient
{
    public sealed class PulletNetworkManager : MonoBehaviour
    {
        [Serializable] public sealed class BoolEvent : UnityEvent<bool> { }
        [Serializable] public sealed class StringEvent : UnityEvent<string> { }
        [Serializable] public sealed class BytesEvent : UnityEvent<byte[]> { }

        public static PulletNetworkManager Instance { get; private set; }

        [SerializeField] private PulletNetworkSettings settings;
        [Header("Unity Events")]
        [SerializeField] private BoolEvent onConnectionChanged = new BoolEvent();
        [SerializeField] private StringEvent onConnectionError = new StringEvent();
        [SerializeField] private BytesEvent onMessageReceived = new BytesEvent();

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private PulletClient _client;
        private CancellationTokenSource _lifetimeCts;
        private bool _resumeShouldReconnect;
        private int _connected;
        private volatile bool _isShuttingDown;

        public bool IsConnected => Volatile.Read(ref _connected) == 1;
        public PulletNetworkSettings Settings => settings;

        public event Action<ConnectedEvent> Connected;
        public event Action<DisconnectedEvent> Disconnected;
        public event Action<ReceivedMessage> MessageReceived;
        public event Action Reconnected;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (settings != null && settings.dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            _lifetimeCts = new CancellationTokenSource();
        }

        private async void Start()
        {
            if (settings != null && settings.connectOnStart)
                await ConnectAndReportAsync();
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                if (!_isShuttingDown)
                    action();
            }
        }

        /// <summary>在 Start 自动连接前注入配置，适合代码创建的启动器或测试场景。</summary>
        public void Configure(PulletNetworkSettings value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (_client != null || IsConnected)
                throw new InvalidOperationException("Cannot replace settings after the client has started.");

            settings = value;
            if (settings.dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        public async Task<ConnectResult> ConnectAsync(string overrideHost = null)
        {
            if (settings == null)
                return ConnectResult.Fail("PulletNetworkSettings is not assigned.");
            if (!settings.Validate(out var validationError, overrideHost))
                return ConnectResult.Fail(validationError);
            if (_isShuttingDown || _lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return ConnectResult.Fail("PulletNet client is shutting down.");

            bool entered = false;
            try
            {
                await _lifecycleGate.WaitAsync(_lifetimeCts.Token);
                entered = true;
                if (_isShuttingDown)
                    return ConnectResult.Fail("PulletNet client is shutting down.");
                if (IsConnected)
                    return ConnectResult.Ok();

                DisposeClient();
                _client = new PulletClient(settings.CreateClientOptions(overrideHost));
                BindClientEvents(_client);
                var result = await _client.ConnectAsync(_lifetimeCts.Token);
                if (result.IsSuccess)
                    SetConnected(true);
                return result;
            }
            catch (OperationCanceledException)
            {
                return ConnectResult.Fail("PulletNet connection was cancelled.");
            }
            catch (Exception ex)
            {
                return ConnectResult.Fail(ex.Message);
            }
            finally
            {
                if (entered)
                    _lifecycleGate.Release();
            }
        }

        public async Task<ConnectResult> DisconnectAsync()
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                if (_client == null)
                    return ConnectResult.Ok();

                var result = await _client.DisconnectAsync();
                if (result.IsSuccess)
                    SetConnected(false);
                return result;
            }
            catch (Exception ex)
            {
                return ConnectResult.Fail(ex.Message);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public ValueTask<ConnectResult> SendAsync(
            byte[] payload,
            ChannelType channelType = ChannelType.ReliableOrdered,
            TransportKind? transportType = null,
            CancellationToken cancellationToken = default)
        {
            if (_client == null || !IsConnected)
                return new ValueTask<ConnectResult>(ConnectResult.Fail("PulletNet client is not connected."));

            return _client.SendAsync(payload, channelType, transportType, cancellationToken);
        }

        public Task<IReadOnlyList<PulletServerInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            if (settings == null)
                throw new InvalidOperationException("PulletNetworkSettings is not assigned.");
            if (!settings.enableDiscovery)
                return Task.FromResult<IReadOnlyList<PulletServerInfo>>(Array.Empty<PulletServerInfo>());

            return PulletServerDiscovery.DiscoverAsync(
                settings.discoveryPort,
                settings.discoveryTimeoutSeconds,
                settings.discoveryServiceType,
                cancellationToken);
        }

        private void BindClientEvents(PulletClient client)
        {
            client.Connected += value =>
            {
                if (!IsCurrentClient(client))
                    return;
                SetConnected(true);
                Enqueue(() =>
                {
                    if (!IsCurrentClient(client)) return;
                    Connected?.Invoke(value);
                    onConnectionChanged.Invoke(true);
                });
            };
            client.Disconnected += value =>
            {
                if (!IsCurrentClient(client))
                    return;
                SetConnected(false);
                Enqueue(() =>
                {
                    if (!IsCurrentClient(client)) return;
                    Disconnected?.Invoke(value);
                    onConnectionChanged.Invoke(false);
                });
            };
            client.Reconnected += () =>
            {
                if (!IsCurrentClient(client))
                    return;
                SetConnected(true);
                Enqueue(() =>
                {
                    if (!IsCurrentClient(client)) return;
                    Reconnected?.Invoke();
                    onConnectionChanged.Invoke(true);
                });
            };
            client.ReconnectFailed += value => Enqueue(() =>
            {
                if (IsCurrentClient(client)) onConnectionError.Invoke(value.Reason);
            });
            client.MessageReceived += value =>
            {
                if (!IsCurrentClient(client))
                    return;
                var bytes = value.Payload.ToArray();
                var safeMessage = new ReceivedMessage(bytes, value.TransportType, value.ChannelType);
                Enqueue(() =>
                {
                    if (!IsCurrentClient(client)) return;
                    MessageReceived?.Invoke(safeMessage);
                    onMessageReceived.Invoke(bytes);
                });
            };
        }

        private bool IsCurrentClient(PulletClient client) => !_isShuttingDown && ReferenceEquals(client, _client);

        private void Enqueue(Action action)
        {
            if (!_isShuttingDown)
                _mainThreadActions.Enqueue(action);
        }

        private void SetConnected(bool value) => Interlocked.Exchange(ref _connected, value ? 1 : 0);

        private async Task ConnectAndReportAsync()
        {
            try
            {
                var result = await ConnectAsync();
                if (!result.IsSuccess)
                    Enqueue(() => onConnectionError.Invoke(result.Message ?? "PulletNet connection failed."));
            }
            catch (Exception ex)
            {
                Enqueue(() => onConnectionError.Invoke(ex.Message));
            }
        }

        private async void OnApplicationPause(bool pauseStatus)
        {
            if (_isShuttingDown || settings == null || !settings.reconnectOnApplicationResume)
                return;

            try
            {
                if (pauseStatus)
                {
                    _resumeShouldReconnect = IsConnected;
                    if (_resumeShouldReconnect)
                        await DisconnectAsync();
                }
                else if (_resumeShouldReconnect)
                {
                    _resumeShouldReconnect = false;
                    await ConnectAndReportAsync();
                }
            }
            catch (Exception ex)
            {
                Enqueue(() => onConnectionError.Invoke(ex.Message));
            }
        }

        private async void OnDestroy()
        {
            if (Instance != this)
                return;

            Instance = null;
            _isShuttingDown = true;
            _lifetimeCts.Cancel();
            try
            {
                await DisconnectAsync();
                DisposeClient();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PulletNet] Disconnect during destroy failed: {ex.Message}");
            }
            finally
            {
                _lifetimeCts.Dispose();
            }
        }

        private void DisposeClient()
        {
            if (_client == null)
                return;

            ((IDisposable)_client).Dispose();
            _client = null;
            SetConnected(false);
        }
    }
}
