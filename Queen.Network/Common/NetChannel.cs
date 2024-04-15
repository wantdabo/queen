using ENet;
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

        public void Send(byte[] data)
        {
            var packet = new Packet();
            packet.Create(data);
            peer.Send(id, ref packet);
            packet.Dispose();
        }
    }
}
