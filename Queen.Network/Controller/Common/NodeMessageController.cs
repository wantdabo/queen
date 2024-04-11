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
        public Channel channel;
        public Type msgType;

        public void Create(Channel channel = null)
        {
            this.channel = channel;
            OnCreate();
        }

        protected abstract void OnCreate();

        public abstract void Receive(Channel channel, object msg);
    }

    public abstract class NodeMessageController<T> : NodeMessageController where T : class
    {
        protected override void OnCreate() 
        {
            this.msgType = typeof(T);
        }

        public override void Receive(Channel channel, object msg)
        {
            if (null != this.channel) if (this.channel.id != channel.id || this.channel.peer.ID != channel.peer.ID) return;

            OnReceive(channel, msg as T);
        }

        protected abstract void OnReceive(Channel channel, T msg);
    }
}