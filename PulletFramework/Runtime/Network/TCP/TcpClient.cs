using System;
using System.Net;
using System.Net.Sockets;

namespace PulletFramework.Network
{
    /// <summary>
    /// Core通用TCP客户端，不依赖PulletNet。Connect/Update和用户回调在Unity主线程。
    /// Dispose是终态：关闭候选连接和通道，抑制迟到回调；不能在Dispose后重新连接。
    /// </summary>
    public class TcpClient : IDisposable
    {
        private sealed class ConnectAttempt
        {
            public Socket Socket;
            public SocketAsyncEventArgs Args;
            public Action<SocketError> Callback;
            public long Version;
            public bool Completed, Retired, ArgsReleased;
            public SocketError Error;
        }

        private readonly object gate = new object();
        private readonly IChannelHelper m_ChannelHelper;
        private readonly ThreadSyncContext m_SyncContext = new ThreadSyncContext();
        private TcpChannel m_Channel;
        private ConnectAttempt pending;
        private Action<SocketError> channelCallback;
        private bool disposed;
        private long version;

        public TcpClient(IChannelHelper channelHelper)
        {
            m_ChannelHelper = channelHelper ?? new DefaultIChannelHelper();
        }

        public void ConnectAsync(string ip, int port, Action<SocketError> callback)
            => ConnectAsync(new IPEndPoint(IPAddress.Parse(ip), port), callback);

        /// <summary>开始一次连接，结果在Update派发。同一实例不接受重叠连接；网络失败后可显式重连。</summary>
        public void ConnectAsync(IPEndPoint remote, Action<SocketError> callback)
        {
            if (remote == null) throw new ArgumentNullException(nameof(remote));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(TcpClient));
                if (pending != null || m_Channel != null) throw new InvalidOperationException("A TCP connection is already pending or active.");
                var attempt = new ConnectAttempt
                {
                    Socket = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp),
                    Args = new SocketAsyncEventArgs { RemoteEndPoint = remote },
                    Callback = callback,
                    Version = ++version
                };
                attempt.Args.UserToken = attempt;
                attempt.Args.Completed += ConnectCompleted;
                pending = attempt;
                try
                {
                    if (!attempt.Socket.ConnectAsync(attempt.Args)) CaptureConnect(attempt);
                }
                catch
                {
                    pending = null;
                    attempt.Completed = true;
                    Retire(attempt);
                    throw;
                }
            }
        }

        private void ConnectCompleted(object sender, SocketAsyncEventArgs args)
        {
            lock (gate) CaptureConnect((ConnectAttempt)args.UserToken);
        }

        private void CaptureConnect(ConnectAttempt attempt)
        {
            attempt.Error = attempt.Args.SocketError;
            attempt.Completed = true;
            if (disposed || attempt.Retired || !ReferenceEquals(pending, attempt))
            {
                Retire(attempt);
                return;
            }
            // 即使同步完成，也在同一个主线程观察点交付；关闭无需等待该队列清理候选Socket。
            m_SyncContext.Post(ProcessConnected, attempt);
        }

        private void ProcessConnected(object state)
        {
            var attempt = (ConnectAttempt)state;
            SocketError result;
            lock (gate)
            {
                if (disposed || !ReferenceEquals(pending, attempt) || attempt.Version != version)
                {
                    Retire(attempt);
                    return;
                }
                pending = null;
                result = attempt.Error;
                if (result == SocketError.Success)
                {
                    var channel = new TcpChannel();
                    channel.channelHandleError = (close, error) => OnChannelError(channel, close);
                    try
                    {
                        channel.InitChannel(attempt.Socket, m_ChannelHelper, m_SyncContext);
                        if (disposed || !channel.IsConnected())
                        {
                            channel.Dispose();
                            result = SocketError.OperationAborted;
                        }
                        else
                        {
                            m_Channel = channel;
                            channelCallback = attempt.Callback;
                        }
                    }
                    catch (Exception error)
                    {
                        channel.Dispose();
                        result = SocketError.SocketError;
                        PLogger.Exception(error, "TCP channel initialization failed.");
                    }
                }
                if (result != SocketError.Success) attempt.Socket.Dispose();
                ReleaseArgs(attempt);
            }
            InvokeCallback(attempt.Callback, result, attempt.Version);
        }

        private void OnChannelError(TcpChannel channel, bool close)
        {
            if (!close) return;
            Action<SocketError> callback;
            long current;
            lock (gate)
            {
                if (disposed || !ReferenceEquals(m_Channel, channel)) return;
                m_Channel = null;
                callback = channelCallback; channelCallback = null;
                current = version;
            }
            channel.Dispose();
            InvokeCallback(callback, SocketError.NotConnected, current);
        }

        private void InvokeCallback(Action<SocketError> callback, SocketError error, long expectedVersion)
        {
            lock (gate)
            {
                if (disposed || expectedVersion != version || callback == null) return;
                try { callback(error); }
                catch (Exception exception) { PLogger.Exception(exception, "TCP connection callback failed."); }
            }
        }

        public void Send(int id) => SendPackage(new NetPackage { msgId = id });

        // 保留旧入口；对象消息的序列化不属于连接层，本批不引入新的序列化策略。
        public void Send(int id, object msg)
        {
            if (msg != null && !(msg is byte[]))
                throw new ArgumentException("Send(id, msg) accepts encoded byte arrays. Serialize objects before sending.", nameof(msg));
            SendPackage(new NetPackage { msgId = id, bodyBytes = (byte[])msg });
        }

        /// <summary>将已编码消息交给当前通道。连接未就绪或已关闭时明确拒绝，不静默丢弃。</summary>
        public void SendPackage(NetPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            TcpChannel channel;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(TcpClient));
                channel = m_Channel ?? throw new InvalidOperationException("TCP connection is not ready.");
            }
            channel.SendPackage(package);
        }

        internal void Update(float deltaTime, float unscaledDeltaTime)
        {
            lock (gate) { if (disposed) return; }
            m_SyncContext.Update();
            TcpChannel channel;
            lock (gate) channel = m_Channel;
            channel?.Update(deltaTime, unscaledDeltaTime);
        }

        internal void Destroy() => Dispose();

        public void Dispose()
        {
            TcpChannel channel;
            lock (gate)
            {
                if (disposed) return;
                disposed = true; version++;
                if (pending != null) { Retire(pending); pending = null; }
                channel = m_Channel; m_Channel = null; channelCallback = null;
            }
            // 不持有客户端锁等待通道锁，避免与通道错误回调形成反向锁序。
            channel?.Dispose();
        }

        private void Retire(ConnectAttempt attempt)
        {
            attempt.Retired = true;
            attempt.Socket.Dispose();
            // 正在进行的Connect仍持有Args，由原生完成回调释放，不能提前销毁。
            if (attempt.Completed) ReleaseArgs(attempt);
        }

        private void ReleaseArgs(ConnectAttempt attempt)
        {
            if (attempt.ArgsReleased) return;
            attempt.ArgsReleased = true;
            attempt.Args.Completed -= ConnectCompleted;
            attempt.Args.Dispose();
        }
    }
}
