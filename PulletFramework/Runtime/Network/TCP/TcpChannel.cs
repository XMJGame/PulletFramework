using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace PulletFramework.Network
{
    /// <summary>
    /// Core TCP的一次连接。Update与业务回调在主线程，IO线程只提交完成记录。
    /// 关闭后拒收新发送；Socket完成前保留缓冲区，不依赖后续Update释放。
    /// </summary>
    internal class TcpChannel : IDisposable
    {
        private sealed class IoCompletion
        {
            public bool Send;
            public int Bytes;
            public SocketError Error;
        }

        public HandleErrorDelegate channelHandleError;
        private readonly object gate = new object();
        private readonly SocketAsyncEventArgs m_ReceiveArgs = new SocketAsyncEventArgs();
        private readonly SocketAsyncEventArgs m_SendArgs = new SocketAsyncEventArgs();
        private readonly Queue<NetPackage> m_SendQueue = new Queue<NetPackage>(1000);
        private readonly Queue<NetPackage> m_ReceiveQueue = new Queue<NetPackage>(1000);
        private readonly SendPackageStream m_SendPackageStream = new SendPackageStream();
        private readonly ReceivePackageStream m_ReceivePackageStream = new ReceivePackageStream();
        private Socket m_Socket;
        private IChannelHelper m_ChannelHelper;
        private ThreadSyncContext m_SyncContext;
        private bool m_IsSending, m_IsReceiving, disposed, resourcesReleased;
        private int pendingIo;
        private float m_HeartBeatTime;
        public IChannelHelper channelHelper => m_ChannelHelper;

        public void InitChannel(Socket socket, IChannelHelper helper, ThreadSyncContext context)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (helper == null) throw new ArgumentNullException(nameof(helper));
            if (context == null) throw new ArgumentNullException(nameof(context));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(TcpChannel));
                if (m_Socket != null) throw new InvalidOperationException("Channel already initialized.");
                m_Socket = socket;
                m_ChannelHelper = helper;
                m_SyncContext = context;
                m_Socket.NoDelay = true;
                if (helper.PackageHeaderLength <= 0) throw new ArgumentOutOfRangeException(nameof(helper.PackageHeaderLength));
                m_ReceivePackageStream.PrepareForPacketHeader(helper.PackageHeaderLength);
                m_SendArgs.Completed += IO_Completed;
                m_ReceiveArgs.Completed += IO_Completed;
                helper.RigistHandleErrorCallback(HandleError);
            }
        }

        public void SendPackage(NetPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(TcpChannel));
                m_SendQueue.Enqueue(package);
            }
        }

        public bool IsConnected()
        {
            lock (gate) return !disposed && m_Socket != null && m_Socket.Connected;
        }

        // 完成记录不借用可被Dispose/reuse的EventArgs。关闭后的物理完成直接清理，不投递无主回调。
        private void IO_Completed(object sender, SocketAsyncEventArgs args)
        {
            lock (gate)
            {
                var result = Capture(args, ReferenceEquals(args, m_SendArgs));
                pendingIo--;
                if (!disposed) m_SyncContext.Post(ApplyCompletion, result);
                ReleaseClosedResources();
            }
        }

        private static IoCompletion Capture(SocketAsyncEventArgs args, bool send)
            => new IoCompletion { Send = send, Bytes = args.BytesTransferred, Error = args.SocketError };

        private void ApplyCompletion(object state)
        {
            lock (gate)
            {
                if (disposed) return;
                var result = (IoCompletion)state;
                if (result.Send) ProcessSend(result);
                else ProcessReceive(result);
            }
        }

        private void ProcessSend(IoCompletion result)
        {
            m_IsSending = false;
            if (result.Error != SocketError.Success || result.Bytes <= 0 || result.Bytes > m_SendPackageStream.count)
            {
                HandleError(true, $"TCP send failed: {result.Error}, bytes={result.Bytes}");
                return;
            }
            // 保留本批次及偏移，只有全部字节发完才允许Reset和取下一批。
            m_SendPackageStream.stream.Position += result.Bytes;
        }

        private void ProcessReceive(IoCompletion result)
        {
            m_IsReceiving = false;
            if (result.Error != SocketError.Success || result.Bytes <= 0 || result.Bytes > m_ReceivePackageStream.count)
            {
                HandleError(true, $"TCP receive ended: {result.Error}, bytes={result.Bytes}");
                return;
            }
            try
            {
                var stream = m_ReceivePackageStream.stream;
                stream.Position += result.Bytes;
                if (stream.Position < stream.Length) return;
                stream.Position = 0;
                if (m_ReceivePackageStream.packageHeader == null)
                {
                    var header = m_ChannelHelper.DeserializePackageHeader(stream);
                    if (header == null || header.PacketLength < 0) throw new InvalidOperationException("Invalid TCP frame header.");
                    m_ReceivePackageStream.PrepareForPacket(header);
                    if (header.PacketLength != 0) return;
                }
                var packet = m_ChannelHelper.DeserializePackage(m_ReceivePackageStream.stream);
                if (packet == null) throw new InvalidOperationException("Invalid TCP frame body.");
                packet.msgId = m_ReceivePackageStream.packageHeader.Id;
                m_ReceiveQueue.Enqueue(packet);
                m_ReceivePackageStream.PrepareForPacketHeader(m_ChannelHelper.PackageHeaderLength);
            }
            catch (Exception error) { HandleError(true, $"TCP frame decode failed: {error.Message}"); }
        }

        internal void Update(float deltaTime, float unscaledDeltaTime)
        {
            lock (gate)
            {
                if (disposed || m_Socket == null) return;
                try
                {
                    m_HeartBeatTime += unscaledDeltaTime;
                    if (m_ChannelHelper.HeartBeatInterval > 0 && m_HeartBeatTime >= m_ChannelHelper.HeartBeatInterval)
                    {
                        m_HeartBeatTime = 0;
                        var heartbeat = m_ChannelHelper.GetHeartBeat();
                        if (heartbeat != null) SendPackage(heartbeat);
                    }
                    if (!disposed) UpdateSending();
                    if (!disposed) UpdateReceiving();
                }
                catch (Exception error) { HandleError(true, $"TCP IO failed: {error.Message}"); }
                while (!disposed && m_ReceiveQueue.Count > 0)
                {
                    var packet = m_ReceiveQueue.Dequeue();
                    try { MessageDistribute.MessageDispose(this, packet); }
                    catch (Exception error) { PLogger.Exception(error, $"TCP message callback failed: {packet.msgId}"); }
                }
            }
        }

        private void UpdateSending()
        {
            if (m_IsSending) return;
            if (m_SendPackageStream.count == 0)
            {
                if (m_SendQueue.Count == 0) return;
                m_SendPackageStream.Reset();
                int count = m_SendQueue.Count;
                for (int i = 0; i < count && !disposed; i++)
                {
                    var packet = m_SendQueue.Dequeue();
                    if (!m_ChannelHelper.Serialize(packet, m_SendPackageStream.stream))
                        throw new InvalidOperationException("TCP serializer rejected a packet.");
                }
                if (disposed) return;
                m_SendPackageStream.stream.Position = 0;
            }
            while (!disposed && m_SendPackageStream.count > 0)
            {
                m_IsSending = true;
                var completion = StartIo(true);
                if (completion == null) return;
                ProcessSend(completion);
            }
        }

        private void UpdateReceiving()
        {
            if (m_IsReceiving) return;
            m_IsReceiving = true;
            var completion = StartIo(false);
            if (completion != null) ProcessReceive(completion);
        }

        // 调用者持有gate。同步完成在当前线程处理，异步完成由IO_Completed接收；两者只结算一次。
        private IoCompletion StartIo(bool send)
        {
            var args = send ? m_SendArgs : m_ReceiveArgs;
            if (send) args.SetBuffer(m_SendPackageStream.stream.GetBuffer(), m_SendPackageStream.offset, m_SendPackageStream.count);
            else args.SetBuffer(m_ReceivePackageStream.stream.GetBuffer(), m_ReceivePackageStream.offset, m_ReceivePackageStream.count);
            pendingIo++;
            try
            {
                bool pending = send ? m_Socket.SendAsync(args) : m_Socket.ReceiveAsync(args);
                if (pending) return null;
                pendingIo--;
                return Capture(args, send);
            }
            catch { pendingIo--; throw; }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                var socket = m_Socket;
                m_Socket = null;
                m_SendQueue.Clear(); m_ReceiveQueue.Clear();
                try { socket?.Dispose(); }
                finally { ReleaseClosedResources(); }
            }
        }

        private void ReleaseClosedResources()
        {
            if (!disposed || pendingIo != 0 || resourcesReleased) return;
            resourcesReleased = true;
            try { m_SendArgs.Dispose(); }
            finally
            {
                try { m_ReceiveArgs.Dispose(); }
                finally
                {
                    try { m_SendPackageStream.Dispose(); }
                    finally { m_ReceivePackageStream.Dispose(); }
                }
            }
        }

        private void HandleError(bool close, string error)
        {
            lock (gate)
            {
                if (disposed) return;
                if (close) Dispose();
                PLogger.Error(error);
                try { channelHandleError?.Invoke(close, error); }
                catch (Exception callbackError) { PLogger.Exception(callbackError, "TCP error callback failed."); }
            }
        }
    }
}
