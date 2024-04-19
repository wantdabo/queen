using ENet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    public class ServerNode : NetNode
    {
        private Dictionary<uint, NetChannel> channelMap = new();

        public ServerNode(string ip, ushort port, int maxConn = 32, int timeout = 15)
        {
            this.ip = ip;
            this.port = port;
            var address = new Address();
            address.SetIP(ip);
            address.Port = port;
            var host = new Host();
            host.Create(address, maxConn);

            var thread = new Thread(() =>
            {
                while (true)
                {
                    if (host.CheckEvents(out var netEvent) <= 0) if (host.Service(timeout, out netEvent) <= 0) continue;
                    switch (netEvent.Type)
                    {
                        case EventType.Connect:
                            var channel = new NetChannel { id = netEvent.ChannelID, peer = netEvent.Peer };
                            channelMap.Add(netEvent.Peer.ID, channel);
                            EmitConnectEvent(channel);
                            break;
                        case EventType.Disconnect:
                            if (channelMap.TryGetValue(netEvent.Peer.ID, out channel))
                            {
                                EmitDisconnectEvent(channel);
                                channelMap.Remove(netEvent.Peer.ID);
                            }
                            break;
                        case EventType.Timeout:
                            if (channelMap.TryGetValue(netEvent.Peer.ID, out channel))
                            {
                                EmitTimeoutEvent(channel);
                                channelMap.Remove(netEvent.Peer.ID);
                            }
                            netEvent.Peer.Disconnect(netEvent.Peer.ID);
                            break;
                        case EventType.Receive:
                            var data = new byte[netEvent.Packet.Length];
                            netEvent.Packet.CopyTo(data);
                            netEvent.Packet.Dispose();
                            if (channelMap.TryGetValue(netEvent.Peer.ID, out channel)) EmitReceiveEvent(channel, data);
                            break;
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
