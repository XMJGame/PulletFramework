using System;
using PulletNet.ClientSDK;
using UnityEngine;
using UnityEngine.Events;

namespace PulletFramework.NetClient
{
    /// <summary>
    /// 可选的 Inspector 事件桥接器。代码模块应直接订阅 PulletNetworkManager 的 C# event；
    /// 只有需要在 Inspector 中拖拽绑定音效、UI 或动画时才挂载此组件。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PulletNetworkManager))]
    public sealed class PulletNetworkEventRelay : MonoBehaviour
    {
        [Serializable] public sealed class BoolEvent : UnityEvent<bool> { }
        [Serializable] public sealed class StringEvent : UnityEvent<string> { }
        [Serializable] public sealed class BytesEvent : UnityEvent<byte[]> { }
        [Serializable] public sealed class ServerListEvent : UnityEvent<PulletServerInfo[]> { }
        [Serializable] public sealed class DiscoveryAttemptEvent : UnityEvent<int, int> { }

        [Tooltip("false 表示首次连接，true 表示内部快速重连成功。")]
        public BoolEvent onConnected = new BoolEvent();
        public StringEvent onConnectFailed = new StringEvent();
        public UnityEvent onDisconnected = new UnityEvent();
        [Tooltip("原始 Payload 会复制为 byte[]；高频业务消息不建议通过 Inspector 处理。")]
        public BytesEvent onMessageReceived = new BytesEvent();
        public StringEvent onNetworkError = new StringEvent();

        public UnityEvent onDiscoveryStarted = new UnityEvent();
        public DiscoveryAttemptEvent onDiscoveryAttempt = new DiscoveryAttemptEvent();
        public ServerListEvent onDiscoverySucceeded = new ServerListEvent();
        public StringEvent onDiscoveryFailed = new StringEvent();
        public UnityEvent onDiscoveryStopped = new UnityEvent();

        private PulletNetworkManager _network;

        private void Awake()
        {
            _network = GetComponent<PulletNetworkManager>();
        }

        private void OnEnable()
        {
            if (_network == null) _network = GetComponent<PulletNetworkManager>();
            _network.Connected += HandleConnected;
            _network.ConnectFailed += HandleConnectFailed;
            _network.Disconnected += HandleDisconnected;
            _network.PayloadReceived += HandlePayload;
            _network.PayloadQueueFaulted += HandleNetworkError;
            _network.Reconnected += HandleReconnected;
            _network.ReconnectFailed += HandleReconnectFailed;
            _network.DiscoveryStarted += HandleDiscoveryStarted;
            _network.DiscoveryAttempted += HandleDiscoveryAttempt;
            _network.DiscoverySucceeded += HandleDiscoverySucceeded;
            _network.DiscoveryFailed += HandleDiscoveryFailed;
            _network.DiscoveryStopped += HandleDiscoveryStopped;
            _network.MessageProtocolError += HandleNetworkError;
        }

        private void OnDisable()
        {
            if (_network == null) return;
            _network.Connected -= HandleConnected;
            _network.ConnectFailed -= HandleConnectFailed;
            _network.Disconnected -= HandleDisconnected;
            _network.PayloadReceived -= HandlePayload;
            _network.PayloadQueueFaulted -= HandleNetworkError;
            _network.Reconnected -= HandleReconnected;
            _network.ReconnectFailed -= HandleReconnectFailed;
            _network.DiscoveryStarted -= HandleDiscoveryStarted;
            _network.DiscoveryAttempted -= HandleDiscoveryAttempt;
            _network.DiscoverySucceeded -= HandleDiscoverySucceeded;
            _network.DiscoveryFailed -= HandleDiscoveryFailed;
            _network.DiscoveryStopped -= HandleDiscoveryStopped;
            _network.MessageProtocolError -= HandleNetworkError;
        }

        private void HandleConnected(ConnectedEvent _) => onConnected?.Invoke(false);
        private void HandleConnectFailed(ConnectFailedEvent value) => onConnectFailed?.Invoke(value.Reason);
        private void HandleDisconnected(DisconnectedEvent _) => onDisconnected?.Invoke();
        private void HandlePayload(ReceivedPayload value) => onMessageReceived?.Invoke(value.Memory.ToArray());
        private void HandleReconnected() => onConnected?.Invoke(true);
        private void HandleReconnectFailed(ReconnectFailedEvent value) => onNetworkError?.Invoke(value.Reason);
        private void HandleNetworkError(string value) => onNetworkError?.Invoke(value);
        private void HandleDiscoveryStarted() => onDiscoveryStarted?.Invoke();
        private void HandleDiscoveryAttempt(int attempt, int maximum) => onDiscoveryAttempt?.Invoke(attempt, maximum);
        private void HandleDiscoverySucceeded(System.Collections.Generic.IReadOnlyList<PulletServerInfo> servers)
        {
            var snapshot = new PulletServerInfo[servers.Count];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = servers[i];
            onDiscoverySucceeded?.Invoke(snapshot);
        }
        private void HandleDiscoveryFailed(string value) => onDiscoveryFailed?.Invoke(value);
        private void HandleDiscoveryStopped() => onDiscoveryStopped?.Invoke();
    }
}
