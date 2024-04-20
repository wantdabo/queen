using Queen.Network.Protocols.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    public class NetNode
    {
        public string? ip;
        public ushort port;
        public bool notify { get; protected set; }

        private Dictionary<Type, List<Delegate>> messageActionMap = new();
        private ConcurrentQueue<NetPackage> netPackages = new();

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

        private void EnqueuePackage(NetChannel channel, Type msgType, INetMessage msg)
        {
            netPackages.Enqueue(new NetPackage { channel = channel, msgType = msgType, msg = msg });
        }

        protected void EmitConnectEvent(NetChannel channel)
        {
            EnqueuePackage(channel, typeof(NodeConnectMessage), new NodeConnectMessage { });
        }

        protected void EmitDisconnectEvent(NetChannel channel)
        {
            EnqueuePackage(channel, typeof(NodeDisconnectMessage), new NodeDisconnectMessage { });
        }

        protected void EmitTimeoutEvent(NetChannel channel)
        {
            EnqueuePackage(channel, typeof(NodeTimeoutMessage), new NodeTimeoutMessage { });
        }

        protected void EmitReceiveEvent(NetChannel channel, byte[] data)
        {
            if (false == ProtoPack.UnPack(data, out var msgType, out var msg)) return;
            EnqueuePackage(channel, msgType, msg);
            if (notify) Notify();
        }

        public void Notify()
        {
            while (netPackages.TryDequeue(out var package))
            {
                if (false == messageActionMap.TryGetValue(package.msgType, out var actions)) return;
                if (null == actions) return;
                for (int i = actions.Count - 1; i >= 0; i--) actions[i].DynamicInvoke(package.channel, package.msg);
            }
        }

        private struct NetPackage
        {
            public NetChannel channel;
            public Type msgType;
            public INetMessage msg;
        }
    }
}
