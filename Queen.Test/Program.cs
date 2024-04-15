using MessagePack;
using Queen.Network.Common;
using Queen.Network.Controller.Common;
using Queen.Network.Protocols;
using Queen.Network.Protocols.Common;
using System.Text;

ENet.Library.Initialize();
Server server = new Server("127.0.0.1", 8888);
server.OnConnect += (channel) =>
{
    Console.WriteLine($"server connect a new client {channel.id}, {channel.peer.ID}");
};
server.OnReceive += (channel, data) =>
{
    if (ProtoPack.UnPack(data, out var msg))
    {
        if (ProtoPack.Pack(new ReqTestMsg { test = 10086 + 1, test2 = 10001 + 1 }, out var bytes)) channel.Send(bytes);
    }
    channel.Send(Encoding.UTF8.GetBytes("hello, my goblin."));
};

Client client = new Client();
client.mc.RegisterMessageController<ReqTestController>();
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
