using MessagePack;

namespace Queen.Protocols.Common;

[MessagePackObject(true)]
public class RPCMessage : INetMessage
{
    public string content { get; set; }
}