using Queen.Network.Common;
using Queen.Protocols.Common;
using Queen.Server.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Queen.Server.Roles.Common
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
        public override string prefix => $"{token}.{role.pid}";
        /// <summary>
        /// 标识
        /// </summary>
        public abstract string token { get; }
        /// <summary>
        /// 玩家
        /// </summary>
        public Role role { get { return actor as Role; } }
    }
}
