using System;
using System.Collections.Concurrent;
using System.Threading;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    /// <summary>后台网络回调到 Unity 主线程的有界队列及会话代际隔离。</summary>
    internal sealed class PulletMainThreadEventPump : IDisposable
    {
        private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        private BoundedPayloadQueue _payloads;
        private long _sessionGeneration;
        private int _pendingReliableOverflows;

        public long DroppedPayloadCount => _payloads?.DroppedCount ?? 0;
        public long ReliablePayloadOverflowCount => _payloads?.ReliableOverflowCount ?? 0;

        public void Reset(int capacity)
        {
            _payloads?.Dispose();
            _payloads = new BoundedPayloadQueue(Math.Max(1, capacity));
            Interlocked.Exchange(ref _pendingReliableOverflows, 0);
        }

        public void Enqueue(Action action) => _actions.Enqueue(action);

        public void EnqueuePayload(ReceivedPayload payload, object client)
        {
            long generation = Volatile.Read(ref _sessionGeneration);
            BoundedPayloadQueue queue = _payloads;
            OwnedPayload owned = payload.ToOwned();
            if (queue == null)
            {
                owned.Dispose();
                return;
            }
            PayloadEnqueueResult result = queue.Enqueue(owned, client, generation);
            if (result == PayloadEnqueueResult.ReliableOverflow)
                Interlocked.Increment(ref _pendingReliableOverflows);
        }

        public void InvalidateSession()
        {
            Interlocked.Increment(ref _sessionGeneration);
            _payloads?.Clear();
        }

        public void DrainActions(bool invoke, Action<Exception> onError)
        {
            while (_actions.TryDequeue(out Action action))
            {
                if (!invoke) continue;
                try { action(); }
                catch (Exception ex) { onError(ex); }
            }
        }

        public void DrainPayloads(
            int maxCallbacks,
            Func<object, bool> isCurrentClient,
            Action<byte[]> onMessage,
            Action<ReceivedPayload> onPayload,
            Action<Exception> onError)
        {
            if (_payloads == null) return;
            for (int i = 0; i < Math.Max(1, maxCallbacks) &&
                            _payloads.TryDequeue(out OwnedPayload owned, out object context, out long generation); i++)
            {
                using (owned)
                {
                    if (!isCurrentClient(context) || generation != Volatile.Read(ref _sessionGeneration))
                        continue;
                    try
                    {
                        byte[] bytes = owned.ToArray();
                        if (generation != Volatile.Read(ref _sessionGeneration)) continue;
                        onMessage(bytes);
                        if (generation != Volatile.Read(ref _sessionGeneration)) continue;
                        onPayload(new ReceivedPayload(owned.Memory, owned.TransportType, owned.ChannelType));
                    }
                    catch (Exception ex) { onError(ex); }
                }
            }
        }

        public int TakeReliableOverflowCount()
            => Interlocked.Exchange(ref _pendingReliableOverflows, 0);

        public void Dispose()
        {
            DrainActions(false, _ => { });
            _payloads?.Dispose();
            _payloads = null;
        }
    }
}
