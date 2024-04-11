using Queen.Network.Protocols;
using Queen.Network.Common;
using Queen.Network.Controller.Common;

public class ReqTestController : NodeMessageController<ReqTestMsg>
{
    protected override void OnReceive(Channel channel, ReqTestMsg msg)
    {
        Console.WriteLine($"client recevice a new msg {channel.id}, {channel.peer.ID}, {msg.test}, {msg.test2}");
    }
}

public class ReqTest2Controller : NodeMessageController<ReqTest2Msg>
{
    protected override void OnReceive(Channel channel, ReqTest2Msg msg)
    {
        Console.WriteLine($"client recevice a new msg {channel.id}, {channel.peer.ID}, {msg.test}, {msg.test2}");
    }
}