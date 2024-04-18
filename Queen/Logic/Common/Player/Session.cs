using Queen.Core;
using Queen.Network.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Common.Player
{
    public class Session : Comp
    {
        public NetChannel channel;

        protected override void OnCreate()
        {
            base.OnCreate();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            channel.peer.Disconnect(channel.peer.ID);
        }
    }
}
