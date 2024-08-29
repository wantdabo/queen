using MessagePack;
using Queen.Protocols.Common;

namespace Queen.Network.Cross;

[MessagePackObject(true)]
public class RPCMessage : INetMessage
{
    public string content { get; set; }
}