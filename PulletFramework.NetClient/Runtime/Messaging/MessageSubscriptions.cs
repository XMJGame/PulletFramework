using System;
using System.Collections.Generic;

namespace PulletFramework.Messaging
{
    /// <summary>一条强类型消息订阅。Unsubscribe 与 Dispose 均可安全重复调用。</summary>
    public interface IMessageSubscription : IDisposable
    {
        uint MessageId { get; }
        Type MessageType { get; }
        bool IsSubscribed { get; }
        void Unsubscribe();
    }

    /// <summary>集中管理一个业务模块创建的全部消息订阅。</summary>
    public sealed class MessageSubscriptionGroup : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<IMessageSubscription> _subscriptions = new List<IMessageSubscription>();
        private bool _disposed;

        public int Count
        {
            get { lock (_gate) return _subscriptions.Count; }
        }

        /// <summary>把订阅交给当前组管理，并原样返回以便调用方保留句柄。</summary>
        public T Add<T>(T subscription) where T : IMessageSubscription
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));
            lock (_gate)
            {
                if (!_disposed)
                {
                    _subscriptions.Add(subscription);
                    return subscription;
                }
            }

            subscription.Unsubscribe();
            throw new ObjectDisposedException(nameof(MessageSubscriptionGroup));
        }

        /// <summary>取消当前组内全部订阅；组本身仍可继续接收新订阅。</summary>
        public void UnsubscribeAll()
        {
            IMessageSubscription[] snapshot;
            lock (_gate)
            {
                snapshot = _subscriptions.ToArray();
                _subscriptions.Clear();
            }
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Unsubscribe();
        }

        public void Dispose()
        {
            IMessageSubscription[] snapshot;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                snapshot = _subscriptions.ToArray();
                _subscriptions.Clear();
            }
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Unsubscribe();
        }
    }
}
