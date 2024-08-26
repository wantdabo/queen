using Queen.Network.Common;
using System.Net;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Queen.Network.Common.Channels
{
    /// <summary>
    /// UDP 通信远程端信息
    /// </summary>
    public class UDPNodeSocket
    {
        /// <summary>
        /// 远程目标
        /// </summary>
        public EndPoint ep { get; private set; }
        /// <summary>
        /// UDPSession
        /// </summary>
        public UDPNode udpnode { get; private set; }

        public UDPNodeSocket(EndPoint ep, UDPNode udpnode)
        {
            this.ep = ep;
            this.udpnode = udpnode;
        }
    }

    /// <summary>
    /// UDPNode 通信渠道
    /// </summary>
    /// <param name="socket">Socket</param>
    public class UDPNodeC(UDPNodeSocket socket) : NetChannel<UDPNodeSocket>(socket)
    {
        public override string id { get => $"{socket.ep.GetIP()}:{socket.ep.GetPort()}"; }

        public override bool alive { get => true; }

        /// <summary>
        /// 发送数据
        /// </summary>
        public override void Send(byte[] data)
        {
            socket.udpnode.Send(socket.ep, data);
        }

        public override void Disconnect()
        {
        }
    }
}
