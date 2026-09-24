using System;
using System.Text;
using Newtonsoft.Json;

namespace PulletFramework.Messaging
{
    public interface IMessageSerializer
    {
        /// <summary>写入消息信封的编码标识。</summary>
        PulletMessageEncoding Encoding { get; }
        /// <summary>把业务对象序列化为消息正文。</summary>
        byte[] Serialize<T>(T value);
        /// <summary>把消息正文恢复为业务对象。</summary>
        T Deserialize<T>(byte[] body);
    }

    /// <summary>SDK 内置 JSON 序列化器。不会读写 .NET 类型元数据。</summary>
    public sealed class JsonMessageSerializer : IMessageSerializer
    {
        public PulletMessageEncoding Encoding => PulletMessageEncoding.Json;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None
        };

        public byte[] Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return EncodingUtf8.GetBytes(JsonConvert.SerializeObject(value, Formatting.None, Settings));
        }

        public T Deserialize<T>(byte[] body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            T value = JsonConvert.DeserializeObject<T>(EncodingUtf8.GetString(body), Settings);
            if (ReferenceEquals(value, null))
                throw new FormatException($"Unable to deserialize message body as {typeof(T).FullName}.");
            return value;
        }

        private static readonly Encoding EncodingUtf8 = new UTF8Encoding(false, true);
    }
}
