using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Protocols.Common
{
    [MessagePackObject(true)]
    public class NodeConnectMessage : INetMessage { }

    [MessagePackObject(true)]
    public class NodeDisconnectMessage : INetMessage { }

    [MessagePackObject(true)]
    public class NodeTimeoutMessage : INetMessage { }

    [MessagePackObject(true)]
    public class NodeReceiveMessage : INetMessage
    {
        public byte[] data;
    }
}
