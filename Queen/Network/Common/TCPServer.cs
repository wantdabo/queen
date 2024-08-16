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
    public class TCPServer : NetNode
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            var service = new TcpService();
            service.Connected = (c, e) =>
            {
                var channel = new NetChannel(c);
                if (false == AddChannel(channel)) return EasyTask.CompletedTask;
                EmitConnectEvent(channel);

                return EasyTask.CompletedTask;
            };
            service.Closed = (c, e) =>
            {
                if (GetChannel(c.Id, out var channel))
                {
                    EmitDisconnectEvent(channel);
                    RmvChannel(channel.id);
                }

                return EasyTask.CompletedTask;
            };
            service.Received = (c, e) =>
            {
                if (GetChannel(c.Id, out var channel)) EmitReceiveEvent(channel, e.ByteBlock.Memory.ToArray());

                return EasyTask.CompletedTask;
            };

            service.Setup(new TouchSocketConfig()
                .SetMaxCount(maxconn)
                .SetThreadCount(sthread)
                .SetListenIPHosts(port)
                .SetTcpDataHandlingAdapter(() => new FixedHeaderPackageAdapter())
            );
            service.Start();
        }
    }
}
