using System;
using System.Collections.Generic;
using System.Threading;

namespace PulletFramework.Messaging
{
    public sealed class PulletMessageRouter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly IMessageSerializer _serializer;
        private readonly Dictionary<uint, List<ISubscription>> _subscriptions =
            new Dictionary<uint, List<ISubscription>>();
        private bool _disposed;

        public PulletMessageRouter(IMessageSerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public IMessageSubscription Subscribe<T>(uint messageId, Action<T> handler)
        {
            if (messageId == 0) throw new ArgumentOutOfRangeException(nameof(messageId));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_subscriptions.TryGetValue(messageId, out List<ISubscription> list))
                {
                    list = new List<ISubscription>();
                    _subscriptions.Add(messageId, list);
                }
                else if (list.Count > 0 && list[0].MessageType != typeof(T))
                {
                    throw new InvalidOperationException(
                        $"Message {messageId} is already registered as {list[0].MessageType.FullName}; " +
                        $"it cannot also be registered as {typeof(T).FullName}.");
                }
                var subscription = new Subscription<T>(this, messageId, handler);
                list.Add(subscription);
                return subscription;
            }
        }

        public bool Dispatch(PulletMessageFrame frame, out string error)
        {
            error = string.Empty;
            if (frame == null) { error = "Message frame is null."; return false; }
            if (frame.Encoding != _serializer.Encoding)
            {
                error = $"No serializer is registered for {frame.Encoding}.";
                return false;
            }

            ISubscription[] snapshot;
            lock (_gate)
            {
                if (_disposed) { error = "Message router is disposed."; return false; }
                if (!_subscriptions.TryGetValue(frame.MessageId, out List<ISubscription> handlers) || handlers.Count == 0)
                    return false;
                snapshot = handlers.ToArray();
            }

            bool handled = false;
            List<string> errors = null;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].Invoke(frame.Body, _serializer);
                    handled = true;
                }
                catch (Exception exception)
                {
                    if (errors == null) errors = new List<string>();
                    errors.Add(exception.Message);
                }
            }
            if (errors != null)
                error = $"Message {frame.MessageId} handler failed: {string.Join(" | ", errors)}";
            return handled;
        }

        public void Dispose()
        {
            ISubscription[] snapshot;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                var all = new List<ISubscription>();
                foreach (List<ISubscription> subscriptions in _subscriptions.Values)
                    all.AddRange(subscriptions);
                snapshot = all.ToArray();
                _subscriptions.Clear();
            }
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Deactivate();
        }

        private void Remove(uint messageId, ISubscription subscription)
        {
            lock (_gate)
            {
                if (_disposed || !_subscriptions.TryGetValue(messageId, out List<ISubscription> list)) return;
                list.Remove(subscription);
                if (list.Count == 0) _subscriptions.Remove(messageId);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PulletMessageRouter));
        }

        private interface ISubscription
        {
            Type MessageType { get; }
            void Invoke(byte[] body, IMessageSerializer serializer);
            void Deactivate();
        }

        private sealed class Subscription<T> : ISubscription, IMessageSubscription
        {
            private PulletMessageRouter _owner;
            private readonly uint _messageId;
            private Action<T> _handler;

            public uint MessageId => _messageId;
            public Type MessageType => typeof(T);
            public bool IsSubscribed => Volatile.Read(ref _owner) != null;

            public Subscription(PulletMessageRouter owner, uint messageId, Action<T> handler)
            {
                _owner = owner;
                _messageId = messageId;
                _handler = handler;
            }

            public void Invoke(byte[] body, IMessageSerializer serializer)
            {
                Action<T> handler = Volatile.Read(ref _handler);
                if (handler != null) handler(serializer.Deserialize<T>(body));
            }

            public void Unsubscribe()
            {
                PulletMessageRouter owner = Interlocked.Exchange(ref _owner, null);
                if (owner == null) return;
                Interlocked.Exchange(ref _handler, null);
                owner.Remove(_messageId, this);
            }

            public void Dispose() => Unsubscribe();

            public void Deactivate()
            {
                Interlocked.Exchange(ref _owner, null);
                Interlocked.Exchange(ref _handler, null);
            }
        }
    }
}
