using Queen.Network.Common;
using TouchSocket.Sockets;

namespace Queen.Network.Common.Channels
{
    /// <summary>
    /// UDPNode 通信渠道
    /// </summary>
    /// <param name="socket">Socket</param>
    public class UDPNodeC(UdpSession socket) : NetChannel<UdpSession>(socket)
    {
        public override string id { get => ""; }
        
        public override bool alive { get => true; }
        
        public override void Send(byte[] data)
        {
            // TODO 可靠 UDP 扩展
            socket.Send(data);
        }
        
        public override void Disconnect()
        {
        }
    }
}
