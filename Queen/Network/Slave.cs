using Queen.Core;
using Queen.Network.Common;
using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network
{
    public class Slave : Comp
    {
        private ServerNode? serverNode;

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.logger.Log("slave create.", ConsoleColor.Cyan);
            serverNode = new(engine.cfg.host, engine.cfg.port, engine.cfg.maxConn);
            engine.logger.Log("slave create success.", ConsoleColor.Cyan);
            engine.logger.Log($"\t\n** hostname: {engine.cfg.hostName} **\n\t\t** ipaddress:{engine.cfg.host} **\n\t\t\t** port:{engine.cfg.port} **");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        public void UnListen<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            serverNode.UnListen(action);
        }

        public void Listen<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            serverNode.Listen(action);
        }
    }
}
