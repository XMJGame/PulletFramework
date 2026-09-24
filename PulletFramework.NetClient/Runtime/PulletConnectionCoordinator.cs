using System;
using System.Threading;

namespace PulletFramework.NetClient
{
    /// <summary>串行化连接操作，并使被新连接或断开取代的异步结果失效。</summary>
    internal sealed class PulletConnectionCoordinator
    {
        internal sealed class ConnectionIntent : IDisposable
        {
            private readonly object _gate = new object();
            private readonly CancellationTokenSource _source;
            private bool _disposed;

            public long Version { get; }
            public CancellationToken Token => _source.Token;

            public ConnectionIntent(long version, CancellationToken lifetimeToken, CancellationToken requestToken)
            {
                Version = version;
                _source = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, requestToken);
            }

            public void Cancel()
            {
                lock (_gate)
                {
                    if (!_disposed) _source.Cancel();
                }
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    _source.Dispose();
                }
            }
        }

        private readonly object _intentGate = new object();
        private ConnectionIntent _activeIntent;
        private long _version;

        /// <summary>同一时刻只允许一个底层 Connect 或 Disconnect 操作。</summary>
        public SemaphoreSlim OperationGate { get; } = new SemaphoreSlim(1, 1);
        public long Version => Volatile.Read(ref _version);

        public ConnectionIntent BeginConnection(CancellationToken lifetimeToken, CancellationToken requestToken = default)
        {
            ConnectionIntent previous;
            ConnectionIntent current;
            lock (_intentGate)
            {
                previous = _activeIntent;
                current = new ConnectionIntent(++_version, lifetimeToken, requestToken);
                _activeIntent = current;
            }
            previous?.Cancel();
            return current;
        }

        public long BeginDisconnect()
        {
            ConnectionIntent previous;
            long version;
            lock (_intentGate)
            {
                previous = _activeIntent;
                _activeIntent = null;
                version = ++_version;
            }
            previous?.Cancel();
            return version;
        }

        public void Complete(ConnectionIntent intent)
        {
            lock (_intentGate)
            {
                if (ReferenceEquals(_activeIntent, intent))
                    _activeIntent = null;
            }
            intent.Dispose();
        }

        public bool IsCurrent(long version) => Version == version;
    }
}
