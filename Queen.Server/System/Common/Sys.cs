using Queen.Server.Common;
using Queen.Server.System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.System.Common
{
    /// <summary>
    /// 系统
    /// </summary>
    public class Sys : Actor
    {
        /// <summary>
        /// 登录
        /// </summary>
        public Login login { get; private set; }

        /// <summary>
        /// 角色俱乐部
        /// </summary>
        public Party party { get; private set; }

        /// <summary>
        /// 房东
        /// </summary>
        public Landlord landlord { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            login = AddBehavior<Login>();
            login.Create();

            party = AddBehavior<Party>();
            party.Create();

            landlord = AddBehavior<Landlord>();
            landlord.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
