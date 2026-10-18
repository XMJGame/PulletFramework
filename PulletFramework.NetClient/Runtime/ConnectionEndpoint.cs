namespace PulletFramework.NetClient
{
    /// <summary>
    /// Immutable connection authority and advertised ports, without Discovery metadata.
    /// Zero denotes an unavailable port. The Manager validates the selected transport;
    /// WebSocket path/TLS and delivery options remain explicit Manager configuration.
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
