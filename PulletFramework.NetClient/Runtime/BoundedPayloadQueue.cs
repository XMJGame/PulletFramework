using System;
using System.Collections.Generic;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    internal enum PayloadEnqueueResult
    {
        Enqueued,
        EnqueuedAfterDroppingOldest,
        DroppedIncoming,
        ReliableOverflow,
        QueueClosed
    }

    /// <summary>Unity 主线程使用的有界 Payload 队列。入队后即接管 OwnedPayload 所有权。</summary>
    internal sealed class BoundedPayloadQueue : IDisposable
    {
        private readonly object _gate = new object();
        private readonly LinkedList<QueuedPayload> _items = new LinkedList<QueuedPayload>();
        private readonly int _capacity;
        private long _droppedCount;
        private long _reliableOverflowCount;
        private bool _disposed;

        public BoundedPayloadQueue(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count
        {
            get { lock (_gate) return _items.Count; }
        }

        public long DroppedCount
        {
            get { lock (_gate) return _droppedCount; }
        }

        public long ReliableOverflowCount
        {
            get { lock (_gate) return _reliableOverflowCount; }
        }

        public PayloadEnqueueResult Enqueue(OwnedPayload payload, object context, long sessionGeneration = 0)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            lock (_gate)
            {
                if (_disposed)
                {
                    payload.Dispose();
                    return PayloadEnqueueResult.QueueClosed;
                }

                if (_items.Count < _capacity)
                {
                    _items.AddLast(new QueuedPayload(payload, context, sessionGeneration));
                    return PayloadEnqueueResult.Enqueued;
                }

                if (!IsDroppable(payload.ChannelType))
                {
                    _reliableOverflowCount++;
                    payload.Dispose();
                    return PayloadEnqueueResult.ReliableOverflow;
                }

                LinkedListNode<QueuedPayload> node = _items.First;
                while (node != null && !IsDroppable(node.Value.Payload.ChannelType))
                    node = node.Next;

                _droppedCount++;
                if (node == null)
                {
                    payload.Dispose();
                    return PayloadEnqueueResult.DroppedIncoming;
                }

                OwnedPayload dropped = node.Value.Payload;
                _items.Remove(node);
                dropped.Dispose();
                _items.AddLast(new QueuedPayload(payload, context, sessionGeneration));
                return PayloadEnqueueResult.EnqueuedAfterDroppingOldest;
            }
        }

        public bool TryDequeue(out OwnedPayload payload, out object context, out long sessionGeneration)
        {
            lock (_gate)
            {
                if (_items.First == null)
                {
                    payload = null;
                    context = null;
                    sessionGeneration = 0;
                    return false;
                }

                payload = _items.First.Value.Payload;
                context = _items.First.Value.Context;
                sessionGeneration = _items.First.Value.SessionGeneration;
                _items.RemoveFirst();
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                while (_items.First != null)
                {
                    OwnedPayload payload = _items.First.Value.Payload;
                    _items.RemoveFirst();
                    payload.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                while (_items.First != null)
                {
                    OwnedPayload payload = _items.First.Value.Payload;
                    _items.RemoveFirst();
                    payload.Dispose();
                }
            }
        }

        private static bool IsDroppable(ChannelType channelType)
        {
            return channelType == ChannelType.Unreliable || channelType == ChannelType.Sequenced;
        }

        private sealed class QueuedPayload
        {
            public QueuedPayload(OwnedPayload payload, object context, long sessionGeneration)
            {
                Payload = payload;
                Context = context;
                SessionGeneration = sessionGeneration;
            }

            public OwnedPayload Payload { get; }
            public object Context { get; }
            public long SessionGeneration { get; }
        }
    }
}
