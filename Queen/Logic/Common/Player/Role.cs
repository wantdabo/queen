using Queen.Network.Common;
using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Common.Player
{
    public class Role : Actor
    {
        public Session session;
        public string pid;
        public string nickName;

        private Dictionary<Delegate, Delegate> actionMap = new();

        public void UnRecv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            if (actionMap.TryGetValue(action, out var callback))
            {
                engine.slave.UnRecv(callback as Action<NetChannel, T>);
                actionMap.Remove(action);
            }
        }

        public void Recv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            Action<NetChannel, T> callback = (c, m) => { if (c.id == session.channel.id && c.peer.ID == session.channel.id) action?.Invoke(c, m); };
            actionMap.Add(action, callback);
            engine.slave.Recv(callback);
        }
    }
}
