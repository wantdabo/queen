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
            UDPServer udpserv = AddComp<UDPServer>();
            udpserv.Initialize("127.0.0.1", 9901, true, 1000, "KEY", 1000);
            udpserv.Create();
            udpserv.Recv<C2SLoginMsg>((c, msg) =>
            {
                
            });

            UDPClient udpclient = AddComp<UDPClient>();
            udpclient.Initialize(true);
            udpclient.Create();
            udpclient.Connect("127.0.0.1", 9901, "KEY");
            udpclient.Send(new C2SLoginMsg { username = "123", password = "456" });
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
