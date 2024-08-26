using Queen.Core;
using Queen.Network.Common;
using Queen.Protocols;
using System.Net;

namespace Queen.Bot.Core
{
    /// <summary>
    /// Queen.Bot 引擎
    /// </summary>
    public class Bot : Engine<Bot>
    {
        /// <summary>
        /// 机器人配置
        /// </summary>
        public Settings settings { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            settings = AddComp<Settings>();
            settings.Create();

            engine.logger.Info($"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}", ConsoleColor.Yellow);
            engine.logger.Info("queen.bot is running...");
            // TCPClient socket = AddComp<TCPClient>();
            // socket.Initialize(false);
            // socket.Create();
            // socket.Connect("127.0.0.1", 12801);
            // socket.Send(new C2SLoginMsg { username = "", password = "" });
            // socket.Send(new C2SLoginMsg());

            var udpnode = AddComp<UDPNode>();
            udpnode.Initialize("127.0.0.1", 8801, true, 4, 1000);
            udpnode.Create();
            udpnode.Recv<C2SLoginMsg>((c, m) =>
            {
                c.Send(new S2CLoginMsg { code = 1, uuid = "testuuid." });
            });

            var udpnode2 = AddComp<UDPNode>();
            udpnode2.Initialize("127.0.0.1", 8802, true, 4, 1000);
            udpnode2.Create();
            udpnode2.Recv<S2CLoginMsg>((c, m) =>
            {

            });
            udpnode2.Send(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8801), new C2SLoginMsg { username = "123", password = "456" });
            udpnode2.Send(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8801), new C2SLoginMsg { username = "123", password = "456" });
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
