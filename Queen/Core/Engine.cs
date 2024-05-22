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
using Queen.Network.Remote;

namespace Queen.Core
{
    /// <summary>
    /// Engine.Execute 事件
    /// </summary>
    public struct EngineExecuteEvent : IEvent { }

    /// <summary>
    /// 引擎组件
    /// </summary>
    public class Engine : Comp
    {
        public Config cfg;
        public Logger logger;
        public Common.Random random;
        public Ticker ticker;
        public Eventor eventor;
        public ObjectPool pool;

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

            // 事件器
            eventor = AddComp<Eventor>();
            eventor.Create();

            // 随机器
            random = AddComp<Common.Random>();
            random.Create();

            // 引擎 Tick
            ticker = AddComp<Ticker>();
            ticker.Create();

            // 对象池
            pool = AddComp<ObjectPool>();
            pool.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ENet.Library.Deinitialize();
        }

        /// <summary>
        /// 引擎运行
        /// </summary>
        public void Run()
        {
            while (true) eventor.Tell<EngineExecuteEvent>();
        }

        /// <summary>
        /// 创建一个引擎
        /// </summary>
        /// <typeparam name="T">引擎类型</typeparam>
        /// <returns>引擎</returns>
        public static T CreateEngine<T>() where T : Engine, new()
        {
            T engine = new();
            engine.engine = engine;
            engine.Create();

            return engine;
        }
    }

    /// <summary>
    /// 引擎
    /// </summary>
    /// <typeparam name="T">引擎类型</typeparam>
    public class Engine<T> : Engine where T : Engine, new()
    {
        public new T engine { get { return base.engine as T; } }
    }
}
