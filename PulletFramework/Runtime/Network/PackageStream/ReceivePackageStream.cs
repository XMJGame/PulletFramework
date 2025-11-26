using System;
using System.IO;

namespace PulletFramework.Network
{
    /// <summary>
/// 接受包流数据
/// </summary>
    public class ReceivePackageStream : IDisposable
    {
        private const int DefaultBufferLength = 1024 * 64;
        private MemoryStream m_Stream;
        private NetPackageHeader m_NetPackageHeader;
        private bool m_Disposed;

        public ReceivePackageStream()
        {
            m_Stream = new MemoryStream(DefaultBufferLength);
            m_NetPackageHeader = null;
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
            get {
                return (int)(m_Stream.Length - m_Stream.Position);
            }
        }

        public NetPackageHeader packageHeader
        {
            get
            {
                return m_NetPackageHeader;
            }
        }

        public void PrepareForPacketHeader(int packetHeaderLength = 8)
        {
            Reset(packetHeaderLength, null);
        }

        public void PrepareForPacket(NetPackageHeader packetHeader)
        {
            if (packetHeader == null)
            {
                throw new Exception("Packet header is invalid.");
            }

            Reset(packetHeader.PacketLength, packetHeader);
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

        private void Reset(int targetLength, NetPackageHeader packetHeader)
        {
            if (targetLength < 0)
            {
                throw new Exception("Target length is invalid.");
            }

            m_Stream.Position = 0L;
            m_Stream.SetLength(targetLength);
            m_NetPackageHeader = packetHeader;
        }

        public void Reset()
        {
            PrepareForPacketHeader();
        }
    }
}
