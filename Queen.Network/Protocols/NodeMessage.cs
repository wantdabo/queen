using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Protocols
{
    [MessagePackObject(true)]
    public class NodeConnectMessage { }

    [MessagePackObject(true)]
    public class NodeDisconnectMessage { }

    [MessagePackObject(true)]
    public class NodeTimeoutMessage { }

    [MessagePackObject(true)]
    public class NodeReceiveMessage { }
}
