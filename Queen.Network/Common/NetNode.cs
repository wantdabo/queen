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

        private Dictionary<Type, List<Delegate>> messageActionMap = new();

        public void UnListen<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            if (false == messageActionMap.TryGetValue(typeof(T), out var actions)) return;
            if (false == actions.Contains(action)) return;

            actions.Remove(action);
        }

        public void Listen<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            if (false == messageActionMap.TryGetValue(typeof(T), out var actions))
            {
                actions = new();
                messageActionMap.Add(typeof(T), actions);
            }

            if (actions.Contains(action)) return;
            actions.Add(action);
        }

        private void Notify(NetChannel channel, Type msgType, INetMessage msg)
        {
            if (false == messageActionMap.TryGetValue(msgType, out var actions)) return;
            if (null == actions) return;
            for (int i = actions.Count - 1; i >= 0; i--) actions[i].DynamicInvoke(channel, msg);
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
