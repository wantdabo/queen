using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Protocols.Common
{
    public interface INetMessage { }

    public class ProtoPack
    {
        private static int INT32_LEN = 4;

        public static bool UnPack(byte[] bytes, out Type? msgType, out INetMessage? msg)
        {
            msg = null;
            msgType = null;
            byte[] header = new byte[INT32_LEN];
            byte[] data = new byte[bytes.Length - INT32_LEN];
            Array.Copy(bytes, header, INT32_LEN);
            Array.Copy(bytes, INT32_LEN, data, 0, bytes.Length - INT32_LEN);

            var msgId = BitConverter.ToInt32(header);

            if (false == messageIdMap.ContainsValue(msgId)) return false;

            var kv = messageIdMap.First(kv => kv.Value.Equals(msgId));
            msgType = kv.Key;
            msg = MessagePackSerializer.Deserialize(kv.Key, data) as INetMessage;

            return true;
        }

        public static bool Pack<T>(T msg, out byte[]? bytes) where T : INetMessage
        {
            bytes = null;
            if (messageIdMap.TryGetValue(msg.GetType(), out var msgId))
            {
                var header = BitConverter.GetBytes(msgId);
                var data = MessagePackSerializer.Serialize(msg);
                bytes = new byte[header.Length + data.Length];
                Array.Copy(header, 0, bytes, 0, header.Length);
                Array.Copy(data, 0, bytes, header.Length, data.Length);

                return true;
            }

            return false;
        }

        private static Dictionary<Type, int> messageIdMap = new()
        {
            {typeof(C2SLoginMsg), 10001},
            {typeof(C2SRegisterMsg), 10002},
            {typeof(S2CLoginMsg), 10003},
            {typeof(S2CRegisterMsg), 10004},
        };
    }
}
