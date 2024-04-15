using ENet;
using Queen.Network.Controller.Common;
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
        public NodeMessageCenter mc;

        public NetNode() 
        {
            mc = new NodeMessageCenter(this);
        }

        public event Action<NetChannel>? OnConnect;
        public void EmitConnectEvent(NetChannel channel) { OnConnect?.Invoke(channel); }

        public event Action<NetChannel>? OnDisconnect;
        public void EmitDisconnectEvent(NetChannel channel) { OnDisconnect?.Invoke(channel); }

        public event Action<NetChannel>? OnTimeout;
        public void EmitTimeoutEvent(NetChannel channel) { OnTimeout?.Invoke(channel); }

        public event Action<NetChannel, byte[]>? OnReceive;
        public void EmitReceiveEvent(NetChannel channel, byte[] data) { OnReceive?.Invoke(channel, data); }
    }
}
