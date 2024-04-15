using Queen.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Core
{
    /// <summary>
    /// 游戏引擎组件
    /// </summary>
    public class Engine : Comp
    {
        public Common.Random random;
        public Ticker ticker;
        public Logger logger;
        public Eventor eventor;
        public ObjectPool pool;
        public GameConfig cfg;

        protected override void OnCreate()
        {
            base.OnCreate();

            // 随机器
            random = AddComp<Common.Random>();
            random.Create();

            // 计时器
            ticker = AddComp<Ticker>();
            ticker.Create();

            // 日志
            logger = AddComp<Logger>();
            logger.Create();

            // 事件
            eventor = AddComp<Eventor>();
            eventor.Create();

            // 对象池
            pool = AddComp<ObjectPool>();
            pool.Create();

            // 配置表
            cfg = AddComp<GameConfig>();
            cfg.Initial();
            cfg.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>
        /// 创建一个游戏引擎
        /// </summary>
        /// <returns>游戏引擎组件</returns>
        public static Engine CreateGameEngine()
        {
            Engine engine = new();
            engine.engine = engine;
            engine.Create();

            return engine;
        }
    }
}
