using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    public class NetNode
    {
        public string? ip;
        public ushort port;

        private Dictionary<Type, List<Action<NetChannel, object>>> messageActionMap = new();

        public void UnListen<T>(Action<NetChannel, object> action) where T : INetMessage
        {
            if (false == messageActionMap.TryGetValue(typeof(T), out var actions)) return;
            if (false == actions.Contains(action)) return;

            actions.Remove(action);
        }

        public void Listen<T>(Action<NetChannel, object> action) where T : INetMessage
        {
            if (false == messageActionMap.TryGetValue(typeof(T), out var actions))
            {
                actions = new();
                messageActionMap.Add(typeof(T), actions);
            }

            if (actions.Contains(action)) return;
            actions.Add(action);
        }
        
        private void Notify(NetChannel channel, Type msgType, object msg)
        {
            if (false == messageActionMap.TryGetValue(msgType, out var actions)) return;
            if (null == actions) return;
            foreach (var action in actions) action?.Invoke(channel, msg);
        }

        protected void EmitConnectEvent(NetChannel channel)
        {
            Notify(channel, typeof(NodeConnectMessage), new NodeConnectMessage { });
        }

        protected void EmitDisconnectEvent(NetChannel channel)
        {
            Notify(channel, typeof(NodeDisconnectMessage), new NodeDisconnectMessage { });
        }

        protected void EmitTimeoutEvent(NetChannel channel)
        {
            Notify(channel, typeof(NodeTimeoutMessage), new NodeTimeoutMessage { });
        }

        protected void EmitReceiveEvent(NetChannel channel, byte[] data)
        {
            if (false == ProtoPack.UnPack(data, out var msgType, out var msg)) return;
            Notify(channel, msgType, msg);
        }
    }
}
