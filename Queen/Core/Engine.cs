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
using Queen.Network;

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
        public Slave slave;

        protected override void OnCreate()
        {
            base.OnCreate();
            // 日志
            logger = AddComp<Logger>();
            logger.Create();

            logger.Log("server initial...", ConsoleColor.Cyan);
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
            // 网络
            ENet.Library.Initialize();
            slave = AddComp<Slave>();
            slave.Create();
            logger.Log("server is running...", ConsoleColor.Green);

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
