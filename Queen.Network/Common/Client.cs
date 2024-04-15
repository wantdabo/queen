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
    public class Client : NetNode
    {
        public Channel channel;

        public void Connect(string ip, ushort port, int timeout = 15)
        {
            this.ip = ip;
            this.port = port;
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
