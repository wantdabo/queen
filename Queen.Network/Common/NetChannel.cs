using ENet;
using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    public class NetChannel
    {
        public byte id;
        public Peer peer;

        public void Send<T>(T msg) where T : INetMessage 
        {
            if (ProtoPack.Pack(msg, out var bytes)) Send(bytes);
        }

        public void Send(byte[] data)
        {
            var packet = new Packet();
            packet.Create(data);
            peer.Send(id, ref packet);
            packet.Dispose();
        }
    }
}
