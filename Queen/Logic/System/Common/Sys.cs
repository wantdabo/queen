using Queen.Logic.Common;
using Queen.Logic.Sys;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.System.Common
{
    public class Sys : Actor
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            AddBehavior<Login>().Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
