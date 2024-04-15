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

        public event Action<Channel>? OnConnect;
        public void EmitConnectEvent(Channel channel) { OnConnect?.Invoke(channel); }

        public event Action<Channel>? OnDisconnect;
        public void EmitDisconnectEvent(Channel channel) { OnDisconnect?.Invoke(channel); }

        public event Action<Channel>? OnTimeout;
        public void EmitTimeoutEvent(Channel channel) { OnTimeout?.Invoke(channel); }

        public event Action<Channel, byte[]>? OnReceive;
        public void EmitReceiveEvent(Channel channel, byte[] data) { OnReceive?.Invoke(channel, data); }
    }
}
