using System;

namespace PulletFramework.Messaging
{
    public enum PulletMessageKind : byte
    {
        Request = 1,
        Response = 2,
        Notification = 3
    }

    public enum PulletMessageEncoding : byte
    {
        Json = 1,
        Protobuf = 2
    }

    /// <summary>业务协议参数。Magic 由业务分配，不属于 PulletNet 传输协议。</summary>
    public sealed class PulletMessageProtocolOptions
    {
        public ushort Magic { get; }
        public byte Version { get; }
        public int MaximumBodyBytes { get; }

        public PulletMessageProtocolOptions(ushort magic, byte version = 1, int maximumBodyBytes = 1024 * 1024)
        {
            if (magic == 0) throw new ArgumentOutOfRangeException(nameof(magic));
            if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (maximumBodyBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
            Magic = magic;
            Version = version;
            MaximumBodyBytes = maximumBodyBytes;
        }
    }

    /// <summary>稳定的 24 字节小端业务信封。</summary>
    public sealed class PulletMessageFrame
    {
        public const int HeaderSize = 24;

        public PulletMessageEncoding Encoding { get; private set; }
        public PulletMessageKind Kind { get; private set; }
        public uint MessageId { get; private set; }
        public ulong CorrelationId { get; private set; }
        public byte[] Body { get; private set; }

        private PulletMessageFrame() { }

        public static byte[] Encode(
            PulletMessageProtocolOptions options,
            PulletMessageEncoding encoding,
            PulletMessageKind kind,
            uint messageId,
            ulong correlationId,
            byte[] body)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (messageId == 0) throw new ArgumentOutOfRangeException(nameof(messageId));
            if (body.Length > options.MaximumBodyBytes)
                throw new ArgumentOutOfRangeException(nameof(body), "Message body exceeds the configured limit.");
            ValidateKindAndCorrelation(kind, correlationId);
            if (encoding != PulletMessageEncoding.Json && encoding != PulletMessageEncoding.Protobuf)
                throw new ArgumentOutOfRangeException(nameof(encoding));

            var output = new byte[HeaderSize + body.Length];
            WriteUInt16(output, 0, options.Magic);
            output[2] = options.Version;
            output[3] = (byte)encoding;
            output[4] = (byte)kind;
            output[5] = 0;
            WriteUInt16(output, 6, 0);
            WriteUInt32(output, 8, messageId);
            WriteUInt64(output, 12, correlationId);
            WriteUInt32(output, 20, checked((uint)body.Length));
            Buffer.BlockCopy(body, 0, output, HeaderSize, body.Length);
            return output;
        }

        public static bool TryDecode(
            PulletMessageProtocolOptions options,
            byte[] payload,
            out PulletMessageFrame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (payload == null || payload.Length < HeaderSize)
                return Fail("Message is shorter than the frame header.", out error);
            if (ReadUInt16(payload, 0) != options.Magic)
                return Fail("Message magic does not match this business protocol.", out error);
            if (payload[2] != options.Version)
                return Fail("Message protocol version is not supported.", out error);
            if (payload[5] != 0 || ReadUInt16(payload, 6) != 0)
                return Fail("Reserved frame bytes must be zero.", out error);

            var encoding = (PulletMessageEncoding)payload[3];
            var kind = (PulletMessageKind)payload[4];
            if (encoding != PulletMessageEncoding.Json && encoding != PulletMessageEncoding.Protobuf)
                return Fail("Message encoding is not supported.", out error);
            if (kind != PulletMessageKind.Request && kind != PulletMessageKind.Response &&
                kind != PulletMessageKind.Notification)
                return Fail("Message kind is invalid.", out error);

            uint bodyLength = ReadUInt32(payload, 20);
            if (bodyLength > options.MaximumBodyBytes || bodyLength > int.MaxValue)
                return Fail("Message body exceeds the configured limit.", out error);
            if (payload.Length != HeaderSize + (int)bodyLength)
                return Fail("Message body length does not match the frame header.", out error);

            ulong correlationId = ReadUInt64(payload, 12);
            try { ValidateKindAndCorrelation(kind, correlationId); }
            catch (ArgumentException exception) { return Fail(exception.Message, out error); }

            var body = new byte[(int)bodyLength];
            Buffer.BlockCopy(payload, HeaderSize, body, 0, body.Length);
            frame = new PulletMessageFrame
            {
                Encoding = encoding,
                Kind = kind,
                MessageId = ReadUInt32(payload, 8),
                CorrelationId = correlationId,
                Body = body
            };
            if (frame.MessageId == 0)
            {
                frame = null;
                return Fail("Message id must be non-zero.", out error);
            }
            return true;
        }

        private static void ValidateKindAndCorrelation(PulletMessageKind kind, ulong correlationId)
        {
            bool valid = kind == PulletMessageKind.Notification ? correlationId == 0 : correlationId != 0;
            if (!valid) throw new ArgumentException("Correlation id does not match the message kind.");
        }

        private static bool Fail(string value, out string error) { error = value; return false; }
        private static ushort ReadUInt16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        private static uint ReadUInt32(byte[] b, int o) =>
            (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        private static ulong ReadUInt64(byte[] b, int o) => ReadUInt32(b, o) | ((ulong)ReadUInt32(b, o + 4) << 32);
        private static void WriteUInt16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void WriteUInt32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }
        private static void WriteUInt64(byte[] b, int o, ulong v)
        {
            WriteUInt32(b, o, (uint)v);
            WriteUInt32(b, o + 4, (uint)(v >> 32));
        }
    }
}
