using System;
using System.Collections.Generic;
using PulletNet.Discovery;

namespace PulletFramework.Discovery
{
    /// <summary>Immutable UI view; sharing a Task result with queued delivery cannot mutate another consumer's DTO.</summary>
    public sealed class DiscoveredServer
    {
        public string InstanceId { get; }
        public string ServerId { get; }
        public string ServerName { get; }
        public string ServiceType { get; }
        public string Host { get; }
        public int TcpPort { get; }
        public int UdpPort { get; }
        public int WebPort { get; }
        internal DiscoveredServer(DiscoveryAdvertisement value)
        {
            InstanceId = value.InstanceId; ServerId = value.ServerId; ServerName = value.ServerName;
            ServiceType = value.ServiceType; Host = value.Host;
            TcpPort = value.TcpPort; UdpPort = value.UdpPort; WebPort = value.WebPort;
        }
    }

    public sealed class DiscoverySnapshot
    {
        public long RoundId { get; }
        public IReadOnlyList<DiscoveredServer> Servers { get; }
        public DiscoveryLimit Limit { get; }
        public int Attempts { get; }
        public int ReceivedDatagrams { get; }
        public int RejectedDatagrams { get; }
        public int NativeCallbackFailures { get; }
        public long RetainedBytes { get; }
        internal DiscoverySnapshot(long id, DiscoveryResult value)
        {
            RoundId = id; Limit = value.Limit; Attempts = value.Attempts;
            ReceivedDatagrams = value.ReceivedDatagrams; RejectedDatagrams = value.RejectedDatagrams;
            NativeCallbackFailures = value.CallbackFailures;
            RetainedBytes = value.RetainedBytes;
            var servers = new DiscoveredServer[value.Servers.Count];
            for (int i = 0; i < servers.Length; i++) servers[i] = new DiscoveredServer(value.Servers[i]);
            Servers = Array.AsReadOnly(servers);
        }
    }

    public sealed class DiscoveryCompletion
    {
        public long RoundId { get; }
        public DiscoverySnapshot Result { get; }
        public Exception Error { get; }
        public bool IsCanceled { get; }
        internal DiscoveryCompletion(long id, DiscoverySnapshot result, Exception error, bool canceled)
        { RoundId = id; Result = result; Error = error; IsCanceled = canceled; }
    }
}
