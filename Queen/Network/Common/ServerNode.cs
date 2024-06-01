using ENet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    /// <summary>
    /// 服务端网络节点
    /// </summary>
    public class ServerNode : NetNode
    {
        /// <summary>
        /// 通信渠道集合
        /// </summary>
        private Dictionary<uint, NetChannel> channelMap = new();

        /// <summary>
        /// 创建服务端网络节点
        /// </summary>
        /// <param name="ip">地址</param>
        /// <param name="port">端口</param>
        /// <param name="notify">是否自动通知消息（子线程）</param>
        /// <param name="maxConn">最大连接数</param>
        /// <param name="timeout">轮询超时</param>
        public ServerNode(string ip, ushort port, bool notify = true, int maxConn = 32, int timeout = 1)
        {
            this.ip = ip;
            this.port = port;
            this.notify = notify;
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
                            channel.peer.Timeout(32, 30000, 30000);
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
                                channel.peer.DisconnectNow(0);
                                EmitDisconnectEvent(channel);
                                channelMap.Remove(netEvent.Peer.ID);
                            }
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
