using PulletNet.ClientSDK;
using UnityEngine;

namespace PulletFramework.NetClient
{
    [CreateAssetMenu(fileName = "PulletNetworkSettings", menuName = "Pullet Framework/NetClient Settings")]
    public sealed class PulletNetworkSettings : ScriptableObject
    {
        [Header("服务器")]
        public string host = "127.0.0.1";
        public int tcpPort = 7778;
        public int udpPort = 7777;
        public int webSocketPort = 7779;
        public string webSocketPath = "/ws";
        public bool webSocketUseTls;

        [Header("连接")]
        public ConnectionMode connectionMode = ConnectionMode.UdpWithTcpControl;
        public bool connectOnStart = true;
        public bool dontDestroyOnLoad = true;
        public bool reconnectOnApplicationResume = true;
        public bool enableKeepAlive = true;
        public bool enableReconnect = true;
        [Min(0)] public int maxReconnectAttempts = 10;

        [Header("客户端标识")]
        public string clientVersion = "0.1.0";
        [Range(0, 255)] public int runtimeId = 1;

        [Header("局域网发现")]
        public bool enableDiscovery = true;
        public int discoveryPort = 58888;
        [Min(0.25f)] public float discoveryTimeoutSeconds = 2f;
        public string discoveryServiceType = "pulletnet";

        public NetClientOptions CreateClientOptions(string overrideHost = null)
        {
            return new NetClientOptions
            {
                Connection = new ConnectionOptions
                {
                    Host = string.IsNullOrWhiteSpace(overrideHost) ? host : overrideHost,
                    TcpPort = tcpPort,
                    UdpPort = udpPort,
                    WsPort = webSocketPort,
                    WsPath = webSocketPath,
                    WsUseTls = webSocketUseTls,
                    ConnectionMode = connectionMode
                },
                Version = new VersionOptions
                {
                    ClientVersion = clientVersion,
                    Runtime = (byte)Mathf.Clamp(runtimeId, 0, 255)
                },
                KeepAlive = new KeepAliveOptions
                {
                    EnableKeepAlive = enableKeepAlive
                },
                Reconnect = new ReconnectOptions
                {
                    EnableReconnect = enableReconnect,
                    MaxReconnectAttempts = Mathf.Max(0, maxReconnectAttempts)
                }
            };
        }

        public bool Validate(out string error, string overrideHost = null)
        {
            if (string.IsNullOrWhiteSpace(overrideHost) && string.IsNullOrWhiteSpace(host))
            {
                error = "Host cannot be empty.";
                return false;
            }

            bool usesTcp = connectionMode == ConnectionMode.TcpOnly || connectionMode == ConnectionMode.UdpWithTcpControl || connectionMode == ConnectionMode.MultiTransport;
            bool usesUdp = connectionMode == ConnectionMode.UdpOnly || connectionMode == ConnectionMode.UdpWithTcpControl || connectionMode == ConnectionMode.UdpWithWebSocketControl || connectionMode == ConnectionMode.MultiTransport;
            bool usesWebSocket = connectionMode == ConnectionMode.WebSocketOnly || connectionMode == ConnectionMode.UdpWithWebSocketControl || connectionMode == ConnectionMode.MultiTransport;
            if ((usesTcp && !IsValidPort(tcpPort)) || (usesUdp && !IsValidPort(udpPort)) || (usesWebSocket && !IsValidPort(webSocketPort)))
            {
                error = "Ports used by the selected connection mode must be between 1 and 65535.";
                return false;
            }

            if (usesWebSocket && (string.IsNullOrWhiteSpace(webSocketPath) || webSocketPath[0] != '/'))
            {
                error = "WebSocket path must start with '/'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(clientVersion))
            {
                error = "Client version cannot be empty.";
                return false;
            }

            if (enableDiscovery && !IsValidPort(discoveryPort))
            {
                error = "Discovery port must be between 1 and 65535.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsValidPort(int port) => port > 0 && port <= 65535;
    }
}
