using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using PulletFramework.Network;
using TcpClient = PulletFramework.Network.TcpClient;

namespace PulletFramework.Tests
{
    public sealed class PulletTcpTests
    {
        private EPulletLogLevel level;
        [SetUp] public void Setup() { level = PLogger.Level; PLogger.Level = EPulletLogLevel.Off; }
        [TearDown] public void Cleanup() { PLogger.Level = level; }
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
        private static void Pump(Func<bool> done, TcpClient client = null)
        {
            var watch = Stopwatch.StartNew();
            while (!done() && watch.Elapsed.TotalSeconds < 5) { client?.Update(0, 0); Thread.Sleep(1); }
            Assert.IsTrue(done(), "Controlled TCP operation did not reach its observation point.");
        }

        private sealed class Helper : IChannelHelper
        {
            private readonly DefaultIChannelHelper codec = new DefaultIChannelHelper();
            public int PackageHeaderLength => 8;
            public int HeartBeatInterval => 0;
            public bool RejectSend, ThrowDecode;
            public int Received;
            public byte[] LastBody;
            public NetPackage GetHeartBeat() => null;
            public bool Serialize<T>(T packet, Stream destination) where T : NetPackage
                => !RejectSend && codec.Serialize(packet, destination);
            public NetPackageHeader DeserializePackageHeader(Stream source)
            {
                if (ThrowDecode) throw new InvalidDataException("Expected invalid header.");
                return codec.DeserializePackageHeader(source);
            }
            public NetPackage DeserializePackage(Stream source)
            { var packet = codec.DeserializePackage(source); LastBody = packet.bodyBytes; Received++; return packet; }
            public object DeSerializeMessage(Type type, byte[] bytes) => bytes;
            public void RigistHandleErrorCallback(HandleErrorDelegate callback) { }
        }

        [Test]
        public void PartialSendKeepsTheBatchUntilEveryByteCompletes()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var channel = new TcpChannel())
            {
                channel.InitChannel(socket, new Helper(), new ThreadSyncContext());
                var buffer = Field<SendPackageStream>(channel, "m_SendPackageStream");
                buffer.stream.Write(new byte[10], 0, 10); buffer.stream.Position = 0;
                channel.SendPackage(new NetPackage { msgId = 10 });
                CompleteSend(channel, 3); Assert.AreEqual(3, buffer.offset); Assert.AreEqual(7, buffer.count);
                CompleteSend(channel, 2); Assert.AreEqual(5, buffer.offset); Assert.AreEqual(5, buffer.count);
                CompleteSend(channel, 5); Assert.AreEqual(10, buffer.offset); Assert.AreEqual(0, buffer.count);
                Assert.AreEqual(1, Field<System.Collections.Generic.Queue<NetPackage>>(channel, "m_SendQueue").Count);
            }
        }

        // 直接提交可控完成记录，不依赖操作系统偶然产生短写，也不向生产类增加测试开关。
        private static void CompleteSend(TcpChannel channel, int bytes)
        {
            var type = typeof(TcpChannel).GetNestedType("IoCompletion", BindingFlags.NonPublic);
            var result = Activator.CreateInstance(type);
            type.GetField("Send").SetValue(result, true); type.GetField("Bytes").SetValue(result, bytes);
            type.GetField("Error").SetValue(result, SocketError.Success);
            typeof(TcpChannel).GetMethod("ApplyCompletion", Private).Invoke(channel, new[] { result });
        }

        [Test]
        public void ZeroProgressSendClosesTheChannelAndRejectsFurtherWork()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var channel = new TcpChannel())
            {
                channel.InitChannel(socket, new Helper(), new ThreadSyncContext());
                int failures = 0; channel.channelHandleError = (_, __) => failures++;
                CompleteSend(channel, 0); Assert.AreEqual(1, failures);
                Assert.Throws<ObjectDisposedException>(() => channel.SendPackage(new NetPackage()));
            }
        }

        [Test]
        public void DefaultHeaderPreservesAll32MessageIdBits()
        {
            var codec = new DefaultIChannelHelper();
            using (var stream = new MemoryStream())
            {
                codec.Serialize(new NetPackage { msgId = 70001, bodyBytes = new byte[] { 1, 2 } }, stream);
                stream.Position = 0; var header = codec.DeserializePackageHeader(stream);
                Assert.AreEqual(70001, header.Id); Assert.AreEqual(2, header.PacketLength);
            }
        }

        [Test]
        public void LateCompletionAfterDisposeCannotTouchReleasedBuffers()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                var channel = new TcpChannel();
                channel.InitChannel(socket, new Helper(), new ThreadSyncContext());
                channel.Dispose();
                Assert.DoesNotThrow(() => CompleteSend(channel, 1));
                Assert.IsTrue(Field<bool>(channel, "resourcesReleased"));
            }
        }

        [Test]
        public void UnconnectedAndDisposedClientsExplicitlyRejectNewWork()
        {
            var client = new TcpClient(new Helper());
            Assert.Throws<InvalidOperationException>(() => client.Send(1));
            Assert.Throws<ArgumentException>(() => client.Send(1, new object()));
            client.Dispose(); client.Dispose();
            Assert.Throws<ObjectDisposedException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), 1, null));
            Assert.Throws<ObjectDisposedException>(() => client.Send(1));
        }

        [Test]
        public void CompletedConnectionIsRetiredWithoutPumpingUnityAfterDispose()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var client = new TcpClient(new Helper());
            System.Net.Sockets.TcpClient peer = null;
            try
            {
                int callbacks = 0;
                var accept = listener.AcceptTcpClientAsync();
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, _ => callbacks++);
                var attempt = Field<object>(client, "pending");
                Pump(() => accept.IsCompleted);
                peer = accept.GetAwaiter().GetResult();
                Pump(() => (bool)attempt.GetType().GetField("Completed").GetValue(attempt));
                client.Dispose();
                Pump(() => (bool)attempt.GetType().GetField("ArgsReleased").GetValue(attempt));
                client.Update(0, 0);
                Assert.AreEqual(0, callbacks); Assert.IsNull(Field<TcpChannel>(client, "m_Channel"));
                Assert.IsNull(Field<object>(client, "pending"));
                Assert.Throws<ObjectDisposedException>(() => client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, null));
            }
            finally { client.Dispose(); peer?.Dispose(); listener.Stop(); }
        }

        [Test]
        public void CloseImmediatelyAfterConnectReleasesCandidateWithoutAnUpdate()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var client = new TcpClient(new Helper());
            try
            {
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, _ => Assert.Fail("Closed attempt must not notify."));
                var attempt = Field<object>(client, "pending");
                client.Dispose();
                Pump(() => (bool)attempt.GetType().GetField("ArgsReleased").GetValue(attempt));
                Assert.IsNull(Field<TcpChannel>(client, "m_Channel"));
            }
            finally { client.Dispose(); listener.Stop(); }
        }

        [Test]
        public void DuplicateConnectDoesNotReplaceAnAcceptedAttempt()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var client = new TcpClient(new Helper());
            try
            {
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, null);
                var attempt = Field<object>(client, "pending");
                Assert.Throws<InvalidOperationException>(() => client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, null));
                Assert.AreSame(attempt, Field<object>(client, "pending"));
            }
            finally { client.Dispose(); listener.Stop(); }
        }

        [Test]
        public void LoopbackTransfersEncodedBytesAndFragmentedReplies()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var helper = new Helper(); var client = new TcpClient(helper);
            System.Net.Sockets.TcpClient peer = null;
            try
            {
                var accept = listener.AcceptTcpClientAsync(); bool connected = false;
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, error => connected = error == SocketError.Success);
                Pump(() => connected && accept.IsCompleted, client); peer = accept.GetAwaiter().GetResult();
                byte[] payload = { 3, 1, 4, 1, 5 };
                client.Send(70001, payload);
                var wire = new byte[13]; var stream = peer.GetStream();
                var read = stream.ReadAsync(wire, 0, wire.Length);
                Pump(() => read.IsCompleted, client);
                int received = read.GetAwaiter().GetResult();
                while (received < wire.Length)
                {
                    read = stream.ReadAsync(wire, received, wire.Length - received); Pump(() => read.IsCompleted, client);
                    int part = read.GetAwaiter().GetResult(); Assert.Greater(part, 0); received += part;
                }
                Assert.AreEqual(5, BitConverter.ToInt32(wire, 0)); Assert.AreEqual(70001, BitConverter.ToInt32(wire, 4));
                CollectionAssert.AreEqual(payload, new ArraySegment<byte>(wire, 8, 5));
                stream.Write(wire, 0, 3); client.Update(0, 0);
                stream.Write(wire, 3, wire.Length - 3);
                Pump(() => helper.Received == 1, client); CollectionAssert.AreEqual(payload, helper.LastBody);
                var channel = Field<TcpChannel>(client, "m_Channel");
                client.Dispose(); Pump(() => Field<bool>(channel, "resourcesReleased"));
            }
            finally { client.Dispose(); peer?.Dispose(); listener.Stop(); }
        }

        [Test]
        public void DecodeFailureAndRemoteCloseAllowExplicitReconnect()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var helper = new Helper { ThrowDecode = true }; var client = new TcpClient(helper);
            System.Net.Sockets.TcpClient peer = null, second = null;
            try
            {
                int connected = 0, failed = 0;
                Action<SocketError> callback = e => { if (e == SocketError.Success) connected++; else failed++; };
                var accept = listener.AcceptTcpClientAsync();
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, callback);
                Pump(() => connected == 1 && accept.IsCompleted, client); peer = accept.GetAwaiter().GetResult();
                peer.GetStream().Write(new byte[8], 0, 8);
                Pump(() => failed == 1, client); Assert.IsNull(Field<TcpChannel>(client, "m_Channel"));
                helper.ThrowDecode = false;
                accept = listener.AcceptTcpClientAsync(); client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, callback);
                Pump(() => connected == 2 && accept.IsCompleted, client); second = accept.GetAwaiter().GetResult();
                second.Dispose(); second = null; Pump(() => failed == 2, client);
            }
            finally { client.Dispose(); peer?.Dispose(); second?.Dispose(); listener.Stop(); }
        }

        [Test]
        public void CallbackCanDisposeTheClientAndExceptionsAreIsolated()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var client = new TcpClient(new Helper());
            try
            {
                bool called = false;
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, _ =>
                { called = true; client.Dispose(); throw new InvalidOperationException("Expected user error."); });
                Pump(() => called, client);
                Assert.IsNull(Field<TcpChannel>(client, "m_Channel"));
            }
            finally { client.Dispose(); listener.Stop(); }
        }

        [Test]
        public void SerializerFailureClosesInsteadOfLeavingAnActiveSend()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var client = new TcpClient(new Helper { RejectSend = true });
            try
            {
                int connected = 0, failures = 0;
                client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, e => { if (e == SocketError.Success) connected++; else failures++; });
                Pump(() => connected == 1, client);
                client.Send(1); Pump(() => failures == 1, client);
                Assert.IsNull(Field<TcpChannel>(client, "m_Channel"));
            }
            finally { client.Dispose(); listener.Stop(); }
        }
    }
}
