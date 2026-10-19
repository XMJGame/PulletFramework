namespace PulletFramework.NetClient
{
    /// <summary>
    /// 不可变的目标主机/端口，不包含发现元数据；端口0代表不可用，由Manager按所选传输校验。
    /// WebSocket路径/TLS与交付选项仍由Manager显式配置，构造端点不等于目标通过认证。
    /// <para>Immutable connection authority and advertised ports without Discovery metadata.
    /// Zero denotes an unavailable port; the Manager validates the selected transport.
    /// WebSocket path/TLS and delivery options remain explicit Manager configuration.</para>
    /// </summary>
    public sealed class ConnectionEndpoint
    {
        public string Host { get; }
        public int TcpPort { get; }
        public int UdpPort { get; }
        public int WebSocketPort { get; }

        public ConnectionEndpoint(string host, int tcpPort, int udpPort, int webSocketPort)
        {
            // Do not validate unused ports or reinterpret missing advertised ports.
            // All connection entrances use the Manager's selected-transport validation.
            Host = host;
            TcpPort = tcpPort;
            UdpPort = udpPort;
            WebSocketPort = webSocketPort;
        }

        public override string ToString()
            => $"{Host} (tcp:{TcpPort}, udp:{UdpPort}, ws:{WebSocketPort})";
    }
}
