using Queen.Core;
using Queen.Logic.Common;
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
            engine.logger.Log("slave create.");
            serverNode = new(engine.cfg.host, engine.cfg.port, false, engine.cfg.maxConn);
            engine.logger.Log("slave create success.");

            engine.eventor.Listen<PreTickEvent>(OnPreTick);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.eventor.UnListen<PreTickEvent>(OnPreTick);
        }

        public void UnRecv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            serverNode.UnListen(action);
        }

        public void Recv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            serverNode.Listen(action);
        }

        private void OnPreTick(PreTickEvent e) 
        {
            serverNode.Notify();
        }
    }
}
