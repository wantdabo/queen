using Queen.Server.Roles.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Bags
{
    /// <summary>
    /// 背包
    /// </summary>
    public class Bag : RoleBehavior<BagInfo, Adapter>
    {
        public override string token => "bag";

        protected override void OnCreate()
        {
            base.OnCreate();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
