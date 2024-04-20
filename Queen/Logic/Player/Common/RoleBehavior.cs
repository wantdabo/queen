using Queen.Logic.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Player.Common
{
    /// <summary>
    /// 玩家行为
    /// </summary>
    public abstract class RoleBehavior : Behavior
    {
        /// <summary>
        /// 玩家
        /// </summary>
        public Role role { get { return actor as Role; } }
    }

    /// <summary>
    /// 玩家行为/ 可存储数据
    /// </summary>
    /// <typeparam name="TDBState">存储数据的类型</typeparam>
    public abstract class RoleBehavior<TDBState> : Behavior<TDBState> where TDBState : IDBState, new()
    {
        /// <summary>
        /// 后缀
        /// </summary>
        protected abstract string suffix { get; }

        /// <summary>
        /// 存储地址
        /// </summary>
        protected override string path => $"{engine.cfg.dataPath}{role.pid}.{suffix}";

        /// <summary>
        /// 玩家
        /// </summary>
        public Role role { get { return actor as Role; } }
    }
}
