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

    }

    public class NodeDisconnectController : NodeMessageController<NodeDisconnectMessage>
    {

    }

    public class NodeTimeoutController : NodeMessageController<NodeTimeoutMessage>
    {

    }

    public class NodeReceiveController : NodeMessageController<NodeReceiveMessage>
    {

    }
}
