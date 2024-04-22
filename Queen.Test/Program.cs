using MessagePack;
using Queen.Network.Common;
using Queen.Network.Protocols;
using Queen.Network.Protocols.Common;
using System.Text;
using System.Threading.Channels;

ENet.Library.Initialize();

ClientNode client = new();
client.Connect("127.0.0.1", 8080);

while (true)
{
    Thread.Sleep(20);
    if (ProtoPack.Pack(new C2SLoginMsg { userName = "queen", password = "queen" }, out var bytes)) client.channel.Send(bytes);
}
Console.ReadKey();