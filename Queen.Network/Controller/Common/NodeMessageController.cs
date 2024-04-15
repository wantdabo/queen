using Queen.Network.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Controller.Common
{
    public abstract class NodeMessageController
    {
        public Type mt;

        public abstract void Receive(NetChannel channel, object msg);
    }

    public abstract class NodeMessageController<T> : NodeMessageController where T : class
    {
        public event Action<NetChannel, T>? OnReceive;

        public NodeMessageController() 
        {
            mt = typeof(T);
        }

        public override void Receive(NetChannel channel, object msg)
        {
            OnReceive?.Invoke(channel, msg as T);
        }
    }
}