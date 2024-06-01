using ENet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    /// <summary>
    /// 客户端网络节点
    /// </summary>
    public class ClientNode : NetNode
    {
        /// <summary>
        /// 通信渠道
        /// </summary>
        public NetChannel channel;

        /// <summary>
        /// 连接服务端网络节点
        /// </summary>
        /// <param name="ip">服务端地址</param>
        /// <param name="port">服务端端口</param>
        /// <param name="notify">是否自动通知消息（子线程）</param>
        /// <param name="timeout">轮询超时</param>
        public void Connect(string ip, ushort port, bool notify = true, int timeout = 1)
        {
            this.ip = ip;
            this.port = port;
            this.notify = notify;
            var address = new Address();
            address.SetIP(ip);
            address.Port = port;
            var host = new Host();
            host.Create();
            channel = new() { id = 0, peer = host.Connect(address) };
            var thread = new Thread(() =>
            {
                while (true)
                {
                    if (host.CheckEvents(out var netEvent) <= 0) if (host.Service(timeout, out netEvent) <= 0) continue;
                    switch (netEvent.Type)
                    {
                        case EventType.Connect:
                            EmitConnectEvent(channel);
                            break;
                        case EventType.Disconnect:
                            EmitDisconnectEvent(channel);
                            break;
                        case EventType.Timeout:
                            EmitTimeoutEvent(channel);
                            break;
                        case EventType.Receive:
                            var data = new byte[netEvent.Packet.Length];
                            netEvent.Packet.CopyTo(data);
                            netEvent.Packet.Dispose();
                            EmitReceiveEvent(channel, data);
                            break;
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
