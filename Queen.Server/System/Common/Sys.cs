using Queen.Server.Common;
using Queen.Server.System;
using System;
using System.Collections.Generic;
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
        protected override void OnCreate()
        {
            base.OnCreate();

            // 登录
            AddBehavior<Login>().Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
