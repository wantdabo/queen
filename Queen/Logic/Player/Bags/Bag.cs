using Queen.Logic.Common;
using Queen.Logic.Player.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Player.Bags
{
    /// <summary>
    /// 背包
    /// </summary>
    public class Bag : RoleBehavior<BagInfo>
    {
        protected override string suffix => "bag";
    }
}
