using Queen.Network.Protocols;
using Queen.Network.Common;
using Queen.Network.Controller.Common;

public class ReqTestController : NodeMessageController<ReqTestMsg>
{
    public ReqTestController()
    {
        OnReceive += (channel, msg) =>
        {
            Console.WriteLine($"client recevice a new msg {channel.id}, {channel.peer.ID}, {msg.test}, {msg.test2}");
        };
    }
}

public class ReqTest2Controller : NodeMessageController<ReqTest2Msg>
{
    public ReqTest2Controller()
    {
        OnReceive += (channel, msg) =>
        {
            Console.WriteLine($"client recevice a new msg {channel.id}, {channel.peer.ID}, {msg.test}, {msg.test2}");
        };
    }
}