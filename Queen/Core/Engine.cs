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
using Queen.Network;

namespace Queen.Core
{
    /// <summary>
    /// Engine.Execute 事件
    /// </summary>
    public struct ExecuteEvent : IEvent { }

    /// <summary>
    /// 引擎组件
    /// </summary>
    public class Engine : Comp
    {
        /// <summary>
        /// 配置表
        /// </summary>
        public Config cfg;
        /// <summary>
        /// 日志
        /// </summary>
        public Logger logger;
        /// <summary>
        /// 事件器
        /// </summary>
        public Eventor eventor;
        /// <summary>
        /// 随机器
        /// </summary>
        public Common.Random random;
        /// <summary>
        /// 事件器
        /// </summary>
        public Ticker ticker;
        /// <summary>
        /// 对象池
        /// </summary>
        public ObjectPool pool;

        protected override void OnCreate()
        {
            base.OnCreate();
            ENet.Library.Initialize();

            cfg = AddComp<Config>();
            cfg.Create();

            logger = AddComp<Logger>();
            logger.Create();

            eventor = AddComp<Eventor>();
            eventor.Create();

            random = AddComp<Common.Random>();
            random.Create();

            ticker = AddComp<Ticker>();
            ticker.Create();

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
            while (true)
            {
                Thread.Sleep(1);
                eventor.Tell<ExecuteEvent>();
            }
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
