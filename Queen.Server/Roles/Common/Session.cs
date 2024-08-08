using Queen.Network.Common;
using Queen.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Common
{
    /// <summary>
    /// 会话
    /// </summary>
    public class Session : Comp
    {
        /// <summary>
        /// 通信渠道
        /// </summary>
        public NetChannel channel;
    }
}
