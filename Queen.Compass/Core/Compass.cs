using Queen.Compass.Stores;
using Queen.Compass.Stores.Common;
using Queen.Core;
using Queen.Network.Cross;

namespace Queen.Compass.Core
{
    public class Compass : Engine<Compass>
    {
        /// <summary>
        /// 服务器配置
        /// </summary>
        public Settings settings { get; set; }
        /// <summary>
        /// RPC
        /// </summary>
        public RPC rpc { get; set; }
        /// <summary>
        /// 第三方数据
        /// </summary>
        public Store store { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            settings = AddComp<Settings>();
            settings.Create();

            rpc = AddComp<RPC>();
            rpc.Initialize(settings.rpchost, settings.rpcport, settings.rpcidlecc, settings.rpctimeout, settings.rpcdeadtime);
            rpc.Create();

            store = AddComp<Store>();
            store.Create();

            engine.logger.Info(
                $"\n\tname: {settings.name}\n\trpchost: {settings.rpchost}\n\trpcport: {settings.rpcport}\n\trpctimeout: {settings.rpctimeout}\n\trpcdeadtime: {settings.rpcdeadtime}"
                , ConsoleColor.Yellow);
            engine.logger.Info("queen.compass is running...");

            var rpc2 = AddComp<RPC>();
            rpc2.Initialize("127.0.0.1", 8802, 5, 5000);
            rpc2.Create();
            Task.Run(() =>
            {
                var point = new PointInfo { name = "queen.bot", ip = "127.0.0.1", port = 8002 };
                var result = rpc2.CrossSync("127.0.0.1", 10001, "COMPASS/SET_POINT", point);
                Console.WriteLine($"{result.state}, {result.content}");
            });

            Console.Title = settings.name;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
