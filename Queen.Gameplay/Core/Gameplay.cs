using Queen.Network;
using Queen.Network.Remote;
using Queen.Core;
using System.Numerics;

namespace Queen.Gameplay.Core
{
    /// <summary>
    /// Queen.Gameplay 引擎
    /// </summary>
    public class Gameplay : Engine<Gameplay>
    {
        /// <summary>
        /// 服务器配置
        /// </summary>
        public Settings settings;
        /// <summary>
        /// RPC
        /// </summary>
        public RPC rpc;
        /// <summary>
        /// 网络
        /// </summary>
        public Slave slave;

        protected override void OnCreate()
        {
            base.OnCreate();
            logger.Log("queen.gameplay initial...");
            settings = AddComp<Settings>();
            settings.Create();

            rpc = AddComp<RPC>();
            rpc.Initialize(RPC.T.GAMEPLAY);
            rpc.Create();

            slave = AddComp<Slave>();
            slave.Initialize(settings.host, settings.port, settings.maxconn);
            slave.Create();

            engine.logger.Log(
                $"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}\n\tmaxconn: {settings.maxconn}"
            , ConsoleColor.Yellow);
            engine.logger.Log("queen.gameplay is running...", ConsoleColor.Green);

            Console.Title = settings.name;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
