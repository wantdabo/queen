using Queen.Common;
using Queen.Network.Common;
using Queen.Protocols.Common;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Queen.Common.Database;
using Queen.Network;
using Sys = Queen.Logic.System.Common.Sys;
using Queen.Logic.Player.Common;
using Queen.Network.Remote;

namespace Queen.Core
{
    /// <summary>
    /// 引擎组件
    /// </summary>
    public class Engine : Comp
    {
        public Config cfg;
        public Common.Random random;
        public Logger logger;
        public Eventor eventor;
        public ObjectPool pool;
        public DBO dbo;
        public RPC rpc;
        public Slave slave;
        public Sys sys;
        public Party party;

        protected override void OnCreate()
        {
            base.OnCreate();
            ENet.Library.Initialize();

            // 配置表
            cfg = AddComp<Config>();
            cfg.Create();

            // 日志
            logger = AddComp<Logger>();
            logger.Create();

            logger.Log("queen initial...");

            // 事件器
            eventor = AddComp<Eventor>();
            eventor.Create();

            // 随机器
            random = AddComp<Common.Random>();
            random.Create();

            // 对象池
            pool = AddComp<ObjectPool>();
            pool.Create();

            // 数据库
            dbo = AddComp<DBO>();
            dbo.Create();

            // RPC
            rpc = AddComp<RPC>();
            rpc.Create();

            // 网络
            slave = AddComp<Slave>();
            slave.Initialize(Settings.host, Settings.port, Settings.maxconn);
            slave.Create();

            // 系统
            sys = AddComp<Sys>();
            sys.Create();

            // 角色
            party = AddComp<Party>();
            party.Create();

            engine.logger.Log(
                $"\n\tname: {Settings.name}\n\tipaddress: {Settings.host}\n\tport: {Settings.port}\n\tmaxconn: {Settings.maxconn}" +
                $"\n\tdbhost: {Settings.dbhost}\n\tdbname: {Settings.dbname}\n\tdbuser: {Settings.dbuser}\n\tdbpwd: {Settings.dbpwd}\n\tdbsave: {Settings.dbsave}"
            , ConsoleColor.Yellow);
            logger.Log("queen is running...", ConsoleColor.Green);

            Console.Title = Settings.name;
            while (true)
            {
                slave.Poll();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ENet.Library.Deinitialize();
        }

        /// <summary>
        /// 创建一个引擎
        /// </summary>
        /// <returns>引擎组件</returns>
        public static Engine CreateEngine()
        {
            Engine engine = new();
            engine.engine = engine;
            engine.Create();

            return engine;
        }
    }
}
