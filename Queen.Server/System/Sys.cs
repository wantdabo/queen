using Queen.Network;
using Queen.Server.Core;
using Queen.Server.System.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adapter = Queen.Network.Adapter;

namespace Queen.Server.System
{
    /// <summary>
    /// 系统
    /// </summary>
    public abstract class Sys : Comp
    {
    }

    /// <summary>
    /// 系统
    /// </summary>
    /// <typeparam name="TAdapter">消息适配器的类型</typeparam>
    public abstract class Sys<TAdapter> : Sys where TAdapter : Adapter, new()
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            var adapter = AddComp<TAdapter>();
            adapter.Initialize(this);
            adapter.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
