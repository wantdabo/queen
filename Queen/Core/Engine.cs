using Queen.Common;
using Queen.Network.Common;
using Queen.Network.Protocols.Common;
using Queen.Network.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Queen.Common.Database;
using Queen.Logic.Player.Common;
using Queen.Network;
using Sys = Queen.Logic.System.Common.Sys;

namespace Queen.Core
{
    /// <summary>
    /// PreTick 事件
    /// </summary>
    public struct PreTickEvent : IEvent { }

    /// <summary>
    /// Tick 事件
    /// </summary>
    public struct TickEvent : IEvent { }

    /// <summary>
    /// LateTick 事件
    /// </summary>
    public struct LateTickEvent : IEvent { }

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
        public Slave slave;
        public Sys sys;
        public Party party;

        protected override void OnCreate()
        {
            base.OnCreate();

            // 日志
            logger = AddComp<Logger>();
            logger.Create();

            logger.Log("server initial...");

            // 事件器
            eventor = AddComp<Eventor>();
            eventor.Create();

            // 配置表
            cfg = AddComp<Config>();
            cfg.Initial();
            cfg.Create();

            // 随机器
            random = AddComp<Common.Random>();
            random.Create();

            // 对象池
            pool = AddComp<ObjectPool>();
            pool.Create();

            // 数据库
            dbo = AddComp<DBO>();
            dbo.Create();

            // 网络
            ENet.Library.Initialize();
            slave = AddComp<Slave>();
            slave.Create();

            // 系统
            sys = AddComp<Sys>();
            sys.Create();

            // 角色
            party = AddComp<Party>();
            party.Create();

            engine.logger.Log(
                $"\n\thostname: {engine.cfg.hostName}\n\tipaddress: {engine.cfg.host}\n\tport: {engine.cfg.port}\n\tmaxconn: {engine.cfg.maxConn}" +
                $"\n\tdbhost: {engine.cfg.dbHost}\n\tdbname: {engine.cfg.dbName}\n\tdbuser: {engine.cfg.dbUser}\n\tdbpwd: {engine.cfg.dbPwd}\n\tdbsave: {engine.cfg.dbSave}"
            , ConsoleColor.Yellow);
            logger.Log("queen is running...", ConsoleColor.Green);

            Console.Title = engine.cfg.hostName;
            // Tick
            while (true)
            {
                Thread.Sleep(cfg.engineTick);
                eventor.Tell<PreTickEvent>();
                eventor.Tell<TickEvent>();
                eventor.Tell<LateTickEvent>();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
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
