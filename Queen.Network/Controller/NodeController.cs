using Queen.Network.Common;
using Queen.Network.Controller.Common;
using Queen.Network.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Controller
{
    public class NodeConnectController : NodeMessageController<NodeConnectMessage>
    {
        protected override void OnReceive(Channel channel, NodeConnectMessage msg)
        {
        }
    }

    public class NodeDisconnectController : NodeMessageController<NodeDisconnectMessage>
    {
        protected override void OnReceive(Channel channel, NodeDisconnectMessage msg)
        {
        }
    }

    public class NodeTimeoutController : NodeMessageController<NodeTimeoutMessage>
    {
        protected override void OnReceive(Channel channel, NodeTimeoutMessage msg)
        {
        }
    }

    public class NodeReceiveController : NodeMessageController<NodeReceiveMessage>
    {
        protected override void OnReceive(Channel channel, NodeReceiveMessage msg)
        {
        }
    }
}
