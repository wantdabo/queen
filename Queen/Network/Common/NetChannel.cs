using ENet;
using Queen.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    /// <summary>
    /// 通信渠道
    /// </summary>
    public class NetChannel
    {
        /// <summary>
        /// ID
        /// </summary>
        public byte id;
        /// <summary>
        /// Peer
        /// </summary>
        public Peer peer;

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="msg">数据</param>
        public void Send<T>(T msg) where T : INetMessage 
        {
            if (ProtoPack.Pack(msg, out var bytes)) Send(bytes);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">二进制数据</param>
        public void Send(byte[] data)
        {
            var packet = new Packet();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.NoAllocate);
            peer.Send(id, ref packet);
            packet.Dispose();
        }
    }
}
