using System;
using System.IO;

namespace PulletFramework.Network
{
    /// <summary>
    /// 发送包处理
    /// </summary>
    public class SendPackageStream : IDisposable
    {
        private const int DefaultBufferLength = 1024 * 64;
        private MemoryStream m_Stream;
        private bool m_Disposed;

        public SendPackageStream()
        {
            m_Stream = new MemoryStream(DefaultBufferLength);
            m_Disposed = false;
        }

        public MemoryStream stream
        {
            get
            {
                return m_Stream;
            }
        }
        public int offset { get { return (int)m_Stream.Position; } }

        public int count
        {
            get
            {
                return (int)(m_Stream.Length - m_Stream.Position);
            }
        }

        public void Reset()
        {
            m_Stream.Position = 0L;
            m_Stream.SetLength(0L);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (m_Disposed)
            {
                return;
            }

            if (disposing)
            {
                if (m_Stream != null)
                {
                    m_Stream.Dispose();
                    m_Stream = null;
                }
            }

            m_Disposed = true;
        }
    }
}