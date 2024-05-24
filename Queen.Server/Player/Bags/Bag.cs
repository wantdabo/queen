﻿using Queen.Server.Player.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Player.Bags
{
    /// <summary>
    /// 背包
    /// </summary>
    public class Bag : RoleBehavior<BagInfo>
    {
        protected override string suffix => "bag";
    }
}