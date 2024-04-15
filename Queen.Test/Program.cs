using MessagePack;
using Queen.Network.Common;
using Queen.Network.Protocols;
using Queen.Network.Protocols.Common;
using System.Text;

ENet.Library.Initialize();
ServerNode server = new("127.0.0.1", 8888);
server.Listen<NodeConnectMessage>((channel, msg) =>
{
    Console.WriteLine($"server connect a new client {channel.id}, {channel.peer.ID}");
});

server.Listen<ReqTestMsg>((channel, msg) =>
{
    if (ProtoPack.Pack(msg, out var bytes)) channel.Send(bytes);
    channel.Send(Encoding.UTF8.GetBytes("hello, my goblin."));
});

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
