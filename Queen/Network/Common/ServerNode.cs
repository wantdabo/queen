using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;

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
        private Dictionary<string, NetChannel> channelDict = new();

        /// <summary>
        /// 创建服务端网络节点
        /// </summary>
        /// <param name="ip">地址</param>
        /// <param name="port">端口</param>
        /// <param name="notify">是否自动通知消息（子线程）</param>
        /// <param name="maxConn">最大连接数</param>
        public ServerNode(string ip, ushort port, bool notify = true, int maxConn = 32)
        {
            this.ip = ip;
            this.port = port;
            this.notify = notify;
            var service = new TcpService();
            service.Connected = (c, e) =>
            {
                var channel = new NetChannel(c);
                channelDict.Add(c.Id, channel);
                EmitConnectEvent(channel);

                return EasyTask.CompletedTask;
            };
            service.Closed = (c, e) =>
            {
                if (channelDict.TryGetValue(c.Id, out var channel))
                {
                    EmitDisconnectEvent(channel);
                    channelDict.Remove(channel.id);
                }

                return EasyTask.CompletedTask;
            };
            service.Received = (c, e) =>
            {
                if (channelDict.TryGetValue(c.Id, out var channel)) EmitReceiveEvent(channel, e.ByteBlock.Memory.ToArray());
                
                return EasyTask.CompletedTask;
            };
            
            service.Setup(new TouchSocketConfig()
                .SetMaxCount(maxConn)
                .SetListenIPHosts(port)
                .SetTcpDataHandlingAdapter(() => new FixedHeaderPackageAdapter())
            );
            service.Start();
        }
    }
}
