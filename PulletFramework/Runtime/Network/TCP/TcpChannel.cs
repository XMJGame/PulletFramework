
using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace PulletFramework.Network
{
    internal class TcpChannel : IDisposable
    {
        public HandleErrorDelegate channelHandleError;
        private readonly SocketAsyncEventArgs m_ReceiveArgs = new SocketAsyncEventArgs();
        private readonly SocketAsyncEventArgs m_SendArgs = new SocketAsyncEventArgs();

        private Queue<NetPackage> m_SendQueue = new Queue<NetPackage>(1000);
        private Queue<NetPackage> m_ReceiveQueue = new Queue<NetPackage>(1000);

        private SendPackageStream m_SendPackageStream = new SendPackageStream();
        private bool m_IsSending = false;
        private ReceivePackageStream m_ReceivePackageStream = new ReceivePackageStream();
        private bool m_IsReceiving = false;

        private float m_HeartBeatTime = 0;

        /// <summary>
        /// 通信Socket
        /// </summary>
        private Socket m_Socket;
        /// <summary>
        /// 包体解析
        /// </summary>
        private IChannelHelper m_ChannelHelper;
        public IChannelHelper channelHelper { get { return m_ChannelHelper; } }
        private ThreadSyncContext m_SyncContext;

        public void InitChannel(Socket socket, IChannelHelper channelHelper, ThreadSyncContext syncContext)
        {
            m_Socket = socket;
            m_Socket.NoDelay = true;
            m_SyncContext = syncContext;

            //编码解码器	
            m_ChannelHelper = channelHelper;
            m_ChannelHelper.RigistHandleErrorCallback(HandleError);
            // 创建IOCP接收类
            m_ReceivePackageStream.Reset();
            m_ReceiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
            m_ReceiveArgs.SetBuffer(m_ReceivePackageStream.stream.GetBuffer(), m_ReceivePackageStream.offset, m_ReceivePackageStream.count);

            // 创建IOCP发送类
            m_SendPackageStream.Reset();
            m_SendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
            m_SendArgs.SetBuffer(m_SendPackageStream.stream.GetBuffer(), m_SendPackageStream.offset, m_SendPackageStream.count);

            m_HeartBeatTime = 0;
        }

        /// <summary>
        /// 发送网络包
        /// </summary>
        public void SendPackage(NetPackage package)
        {
            lock (m_SendQueue)
            {
                m_SendQueue.Enqueue(package);
            }
        }

        /// <summary>
        /// 检测Socket是否已连接
        /// </summary>
        public bool IsConnected()
        {
            if (m_Socket == null)
                return false;
            return m_Socket.Connected;
        }

        /// <summary>
        /// 每当在套接字上完成接收或发送操作时，就调用此方法
        /// </summary>
        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // PLogger.Log("SocketAsyncOperation:" + e.LastOperation.ToString());
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    m_SyncContext.Post(ProcessReceive, e);
                    break;
                case SocketAsyncOperation.Send:
                    m_SyncContext.Post(ProcessSend, e);
                    break;
                case SocketAsyncOperation.Disconnect:
                    PLogger.Error("断开连接了");
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        #region 接受消息
        /// <summary>
        /// 数据接收完成时
        /// </summary>
        private void ProcessReceive(object obj)
        {
            if (m_Socket == null)
                return;

            SocketAsyncEventArgs e = obj as SocketAsyncEventArgs;

            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                m_ReceivePackageStream.stream.Position += e.BytesTransferred;

                //长度不够
                if (m_ReceivePackageStream.stream.Position < m_ReceivePackageStream.stream.Length)
                {
                    m_IsReceiving = false;
                    return;
                }

                m_ReceivePackageStream.stream.Position = 0L;

                bool processSuccess = false;
                //获取包头
                if (m_ReceivePackageStream.packageHeader == null)
                {
                    processSuccess = ProcessPacketHeader();
                }
                else
                {
                    //获取包体
                    processSuccess = ProcessPacket();
                }

                if (processSuccess)
                {
                    m_IsReceiving = false;
                    return;
                }
            }
            else
            {
                HandleError(true, $"ProcessReceive error : {e.SocketError}");
            }
        }

        private bool ProcessPacketHeader()
        {
            try
            {
                NetPackageHeader packetHeader = m_ChannelHelper.DeserializePackageHeader(m_ReceivePackageStream.stream);
                if (packetHeader != null)
                {
                    m_ReceivePackageStream.PrepareForPacket(packetHeader);

                    //没有消息体
                    if (packetHeader.PacketLength <= 0)
                    {
                        bool processSuccess = ProcessPacket();
                        return processSuccess;
                    }
                }
                else
                {
                    //
                    throw new Exception("Packet header is invalid.");
                }
            }
            catch (Exception exception)
            {
                PLogger.Error(exception.Message);
                return false;
                throw;
            }
            return true;
        }

        private bool ProcessPacket()
        {
            try
            {
                NetPackage packet = m_ChannelHelper.DeserializePackage(m_ReceivePackageStream.stream);
                if (packet != null)
                {
                    packet.msgId = m_ReceivePackageStream.packageHeader.Id;
                    //分发消息
                    m_ReceiveQueue.Enqueue(packet);
                }

                m_ReceivePackageStream.PrepareForPacketHeader();
            }
            catch (Exception exception)
            {
                PLogger.Error(exception.Message);
                return false;

                throw;
            }

            return true;
        }

        #endregion

        #region 发送消息
        /// <summary>
		/// 数据发送完成时
		/// </summary>
		private void ProcessSend(object obj)
        {
            if (m_Socket == null)
                return;

            SocketAsyncEventArgs e = obj as SocketAsyncEventArgs;
            if (e.SocketError == SocketError.Success)
            {
                m_IsSending = false;
            }
            else
            {
                HandleError(true, $"ProcessSend error : {e.SocketError}");
            }
        }

        #endregion

        internal void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (m_Socket == null || m_Socket.Connected == false)
                return;
            //发送心跳包
            SendHeartBeat(unscaledDeltaTime);

            // 发送数据
            UpdateSending();

            // 接收数据
            UpdateReceiving();

            //处理接受到的消息
            ReceivingDispose();
        }

        private void SendHeartBeat(float unscaledDeltaTime)
        {
            m_HeartBeatTime += unscaledDeltaTime;
            if (m_HeartBeatTime >= m_ChannelHelper.HeartBeatInterval)
            {
                m_HeartBeatTime = 0;
                if (m_ChannelHelper.GetHeartBeat() != null)
                {
                    SendPackage(m_ChannelHelper.GetHeartBeat());
                }
            }
        }

        private void UpdateSending()
        {
            if (m_IsSending == false && m_SendQueue.Count > 0)
            {
                m_IsSending = true;

                // 清空缓存
                m_SendPackageStream.Reset();
                //// 合并数据一起发送
                while (m_SendQueue.Count > 0)
                {
                    NetPackage packet = null;
                    lock (m_SendQueue)
                    {
                        packet = m_SendQueue.Dequeue();
                    }

                    bool serializeResult = false;
                    try
                    {
                        PLogger.Log($"发送消息：" + packet.msgId);
                        serializeResult = m_ChannelHelper.Serialize(packet, m_SendPackageStream.stream);
                    }
                    catch (Exception exception)
                    {
                        PLogger.Error(exception.Message);
                        throw;
                    }
                }

                m_SendPackageStream.stream.Position = 0;

                // 请求操作
                m_SendArgs.SetBuffer(m_SendPackageStream.stream.GetBuffer(), m_SendPackageStream.offset, m_SendPackageStream.count);
                bool willRaiseEvent = m_Socket.SendAsync(m_SendArgs);
                if (!willRaiseEvent)
                {
                    ProcessSend(m_SendArgs);
                }
            }
        }

        private void UpdateReceiving()
        {
            if (!m_IsReceiving)
            {
                m_IsReceiving = true;

                // 请求操作
                m_ReceiveArgs.SetBuffer(m_ReceivePackageStream.stream.GetBuffer(), m_ReceivePackageStream.offset, m_ReceivePackageStream.count);
                bool willRaiseEvent = m_Socket.ReceiveAsync(m_ReceiveArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(m_ReceiveArgs);
                }
            }
        }

        /// <summary>
        /// 接受的消息处理
        /// </summary>
        private void ReceivingDispose()
        {
            while (m_ReceiveQueue.Count > 0)
            {
                NetPackage packet = null;
                lock (m_ReceiveQueue)
                {
                    packet = m_ReceiveQueue.Dequeue();
                }

                MessageDistribute.MessageDispose(this, packet);
            }
        }

        public void Dispose()
        {
            try
            {
                if (m_Socket != null)
                    m_Socket.Shutdown(SocketShutdown.Both);

                m_SendArgs.Dispose();
                m_ReceiveArgs.Dispose();

                m_SendQueue.Clear();
                m_ReceiveQueue.Clear();

                m_SendPackageStream.Dispose();
                m_ReceivePackageStream.Dispose();

                m_IsSending = false;
                m_IsReceiving = false;

            }
            catch (Exception)
            {
                // throws if client process has already closed, so it is not necessary to catch.
            }
            finally
            {
                if (m_Socket != null)
                {
                    m_Socket.Close();
                    m_Socket = null;
                }
            }
        }

        /// <summary>
        /// 捕获异常错误
        /// </summary>
        private void HandleError(bool isReconnection, string error)
        {
            PLogger.Error(error);
            channelHandleError?.Invoke(isReconnection, error);
        }
    }
}
