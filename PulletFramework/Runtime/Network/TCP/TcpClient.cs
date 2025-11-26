
using System;
using System.Net;
using System.Net.Sockets;

namespace PulletFramework.Network
{
    public class TcpClient : IDisposable
    {
        private class UserToken
        {
            public System.Action<SocketError> Callback;
        }
        private TcpChannel m_Channel;
        private IChannelHelper m_ChannelHelper;
        private ThreadSyncContext m_SyncContext;
        private IPEndPoint m_RemoteEndPoint;

        private System.Action<SocketError> socketErrorCallback;
        public TcpClient(IChannelHelper channelHelper)
        {

            m_ChannelHelper = channelHelper;
            if (m_ChannelHelper == null)
            {
                m_ChannelHelper = new DefaultIChannelHelper();
            }
            m_SyncContext = new ThreadSyncContext();
        }

        public void ConnectAsync(string ip, int port, System.Action<SocketError> callback)
        {
            var remote = new IPEndPoint(IPAddress.Parse(ip), port);
            ConnectAsync(remote, callback);
        }

        /// <summary>
        /// 异步连接
        /// </summary>
        /// <param name="remote">IP终端</param>
        /// <param name="callback">连接回调</param>
        public void ConnectAsync(IPEndPoint remote, System.Action<SocketError> callback)
        {
            socketErrorCallback = callback;
            UserToken token = new UserToken()
            {
                Callback = callback,
            };

            m_RemoteEndPoint = remote;

            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = remote;
            args.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
            args.UserToken = token;

            Socket clientSock = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            bool willRaiseEvent = clientSock.ConnectAsync(args);
            if (!willRaiseEvent)
            {
                ProcessConnected(args);
            }

        }

        /// <summary>
        /// 发送网络包
        /// </summary>

        public void Send(int id)
        {
            NetPackage package = new NetPackage();
            package.msgId = id;
            if (m_Channel != null)
                m_Channel.SendPackage(package);
        }

        public void Send(int id, object msg)
        {
            NetPackage package = new NetPackage();
            package.msgId = id;
            //package.bodyBytes = m_PackageSerializeHelper.Serialize(msg);
            if (m_Channel != null)
                m_Channel.SendPackage(package);
        }

        /// <summary>
        /// 发送网络包
        /// </summary>
        public void SendPackage(NetPackage package)
        {
            if (m_Channel != null)
                m_Channel.SendPackage(package);
        }

        /// <summary>
        /// 处理连接请求
        /// </summary>
        private void ProcessConnected(object obj)
        {
            SocketAsyncEventArgs socketAsync = obj as SocketAsyncEventArgs;
            UserToken token = (UserToken)socketAsync.UserToken;
            if (socketAsync.SocketError == SocketError.Success)
            {
                if (m_Channel != null)
                    throw new Exception("TcpClient channel is created.");

                // 创建频道
                m_Channel = new TcpChannel();
                m_Channel.InitChannel(socketAsync.ConnectSocket, m_ChannelHelper, m_SyncContext);
                m_Channel.channelHandleError = OnChannelHandleError;
            }
            else
            {
                //重连
               // ConnectAsync(m_RemoteEndPoint, token.Callback);
                PLogger.Error($"Network connecte error : {socketAsync.SocketError}");
            }

            // 回调函数		
            if (token.Callback != null)
                token.Callback.Invoke(socketAsync.SocketError);
        }

        private void OnChannelHandleError(bool isDispose, string error)
        {
            if (isDispose) 
            {
                Dispose();
                socketErrorCallback(SocketError.NotConnected);
            }
        }

        private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            //PLogger.Log("AcceptEventArg_Completed:" + e.LastOperation.ToString());
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Connect:
     
                    m_SyncContext.Post(ProcessConnected, e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a connect");
            }
        }

        /// <summary>
        /// 更新网络
        /// </summary>
        internal void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (m_SyncContext != null)
                m_SyncContext.Update();

            if (m_Channel != null)
                m_Channel.Update(deltaTime, unscaledDeltaTime);
        }

        /// <summary>
        /// 销毁网络
        /// </summary>
        internal void Destroy()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (m_Channel != null)
            {
                m_Channel.Dispose();
                m_Channel = null;
            }
        }
    }
}
