using MessagePack;
using Queen.Network.Common;
using Queen.Network.Protocols;
using Queen.Network.Protocols.Common;
using System.Text;
using System.Threading.Channels;

ENet.Library.Initialize();
ServerNode server = new("127.0.0.1", 8888);
server.Listen<NodeConnectMessage>((channel, msg) =>
{
    Console.WriteLine($"server connect a new client {channel.id}, {channel.peer.ID}");
});
Action<NetChannel, ReqTestMsg> action = null;
action = (NetChannel c, ReqTestMsg msg) =>
{
    if (ProtoPack.Pack(new ReqTestMsg { test = 10086, test2 = 10001 }, out var bytes)) c.Send(bytes);
    c.Send(Encoding.UTF8.GetBytes("hello, my goblin."));
    //server.UnListen(action);
};
server.Listen(action);

ClientNode client = new();
client.Listen<ReqTestMsg>((c, m) =>
{
    var msg = m as ReqTestMsg;
    Console.WriteLine($"receive a new message {c.id}, {c.peer.ID}, {msg.test}, {msg.test2}");
});
client.Connect("127.0.0.1", 8888);

Thread t = new Thread(() =>
{
    while (true)
    {
        Thread.Sleep(200);
        if (ProtoPack.Pack(new ReqTestMsg { test = 10086, test2 = 10001 }, out var bytes)) client.channel.Send(bytes);
    }
});
t.IsBackground = true;
t.Start();

Console.ReadKey();
