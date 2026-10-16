using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    /// <summary>Unity handoff limits, copied at construction; bytes mean effective payload bytes, not RSS.</summary>
    public sealed class PulletClientRuntimeOptions
    {
        public int MaximumClients { get; set; } = 32;
        public int MaximumPayloads { get; set; } = 4096;
        public long MaximumPayloadBytes { get; set; } = 16L * 1024 * 1024;
        public int PayloadsPerClient { get; set; } = 1024;
        public long PayloadBytesPerClient { get; set; } = 4L * 1024 * 1024;
        public int PayloadCallbacksPerFrame { get; set; } = 256;
        public long PayloadBytesPerFrame { get; set; } = 1024 * 1024;
        public int LifecycleEventsPerClient { get; set; } = 128;
        public int LifecycleCallbacksPerFrame { get; set; } = 64;

        internal ClientDispatchLimits FreezePayloads()
        {
            if (LifecycleEventsPerClient <= 0 || LifecycleCallbacksPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(PulletClientRuntimeOptions));
            return new ClientDispatchLimits
            {
                MaximumSources = MaximumClients, MaximumPayloads = MaximumPayloads, MaximumBytes = MaximumPayloadBytes,
                PayloadsPerSource = PayloadsPerClient, BytesPerSource = PayloadBytesPerClient,
                CallbacksPerFrame = PayloadCallbacksPerFrame, BytesPerFrame = PayloadBytesPerFrame
            }.Freeze();
        }
    }

    public enum PulletClientLifecycleKind { StateChanged, Connected, ConnectFailed, Disconnected, ReconnectAttempt, Reconnected, ReconnectFailed }
    public enum PulletClientDeliveryFailureReason { ReliableOverflow, CopyFailure, LifecycleOverflow }

    /// <summary>Historical lifecycle fact. Detail is the corresponding SDK event struct; Reconnected has null Detail.</summary>
    public sealed class PulletClientLifecycleEvent
    {
        public HostedClient Source { get; }
        public long DispatchGeneration { get; }
        public PulletClientLifecycleKind Kind { get; }
        public object Detail { get; }
        internal PulletClientLifecycleEvent(HostedClient source, long generation, PulletClientLifecycleKind kind, object detail)
        { Source = source; DispatchGeneration = generation; Kind = kind; Detail = detail; }
    }

    /// <summary>Borrowed payload; retain with Payload.ToOwned and Dispose the retained copy yourself.</summary>
    public readonly struct PulletClientPayloadEvent
    {
        public HostedClient Source { get; }
        public long DispatchGeneration { get; }
        public ReceivedPayload Payload { get; }
        internal PulletClientPayloadEvent(HostedClient source, long generation, ReceivedPayload payload)
        { Source = source; DispatchGeneration = generation; Payload = payload; }
    }

    /// <summary>Delivery is incomplete; only this source is retired. Cleanup is tracked even if no frame is pumped.</summary>
    public sealed class PulletClientDeliveryFailure
    {
        public HostedClient Source { get; }
        public PulletClientDeliveryFailureReason Reason { get; }
        public Exception Error { get; }
        public Task<PulletClientCloseResult> Cleanup { get; }
        internal PulletClientDeliveryFailure(HostedClient source, PulletClientDeliveryFailureReason reason,
            Exception error, Task<PulletClientCloseResult> cleanup)
        { Source = source; Reason = reason; Error = error; Cleanup = cleanup; }
    }

    public sealed class PulletClientCallbackError
    {
        public HostedClient Source { get; }
        public Exception Error { get; }
        internal PulletClientCallbackError(HostedClient source, Exception error) { Source = source; Error = error; }
    }

    public sealed class PulletClientCloseResult
    {
        public HostedClient Source { get; }
        public PulletClientDeliveryFailure DeliveryFailure { get; }
        public Exception CleanupError { get; }
        public bool IsSuccess => DeliveryFailure == null && CleanupError == null;
        internal PulletClientCloseResult(HostedClient source, PulletClientDeliveryFailure failure, Exception error)
        { Source = source; DeliveryFailure = failure; CleanupError = error; }
    }

    public sealed class PulletClientRuntimeCloseResult
    {
        public IReadOnlyList<PulletClientCloseResult> Clients { get; }
        public Exception HostCleanupError { get; }
        public bool IsSuccess { get; }
        internal PulletClientRuntimeCloseResult(PulletClientCloseResult[] clients, Exception hostError)
        {
            Clients = Array.AsReadOnly(clients); HostCleanupError = hostError;
            bool success = hostError == null;
            foreach (var client in clients) success &= client.IsSuccess;
            IsSuccess = success;
        }
    }

    public readonly struct PulletClientRuntimeSnapshot
    {
        public int RegisteredClients { get; }
        public int Payloads { get; }
        public long PayloadBytes { get; }
        public int PreparingPayloads { get; }
        public int ConsumingPayloads { get; }
        public int LifecycleEvents { get; }
        public long DroppedPayloads { get; }
        public long DiagnosticHandlerFailures { get; }
        internal PulletClientRuntimeSnapshot(int clients, ClientDispatchSnapshot payloads, int lifecycle, long diagnosticErrors)
        {
            RegisteredClients = clients; Payloads = payloads.Payloads; PayloadBytes = payloads.Bytes;
            PreparingPayloads = payloads.Preparing; ConsumingPayloads = payloads.Consuming;
            LifecycleEvents = lifecycle; DroppedPayloads = payloads.Dropped; DiagnosticHandlerFailures = diagnosticErrors;
        }
    }
}
