using Queen.Network;
using Queen.Remote;
using Queen.Core;
using System.Numerics;
using Queen.Gameplay.Game;
using System.IO;

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
        /// <summary>
        /// 导演
        /// </summary>
        public Director director;

        protected override void OnCreate()
        {
            base.OnCreate();
            logger.Log("queen.gameplay initial...");
            settings = AddComp<Settings>();
            settings.Create();

            rpc = AddComp<RPC>();
            rpc.Initialize(RPC.TAR.GAMEPLAY);
            rpc.Create();

            slave = AddComp<Slave>();
            slave.Initialize(settings.host, settings.port, settings.maxconn);
            slave.Create();

            director = AddComp<Director>();
            director.Create();

            engine.logger.Log(
                $"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}\n\tmaxconn: {settings.maxconn}" +
                $"\n\tframe: {settings.frame}"
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
