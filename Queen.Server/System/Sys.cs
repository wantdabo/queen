using Queen.Network;
using Queen.Network.Common;
using Queen.Protocols.Common;
using Queen.Server.Core;
using Queen.Server.System.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        protected TAdapter adapter;

        protected override void OnCreate()
        {
            base.OnCreate();
            adapter = AddComp<TAdapter>();
            adapter.Initialize(this);
            adapter.Create();
            NetRecv();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            NetUnRecv();
        }

        private void NetRecv()
        {
            var methods = adapter.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if(null == method.GetCustomAttribute<NetBinding>()) continue;
                var ps = method.GetParameters();
                if (2 != ps.Length) continue;
                if (ps[0].ParameterType != typeof(NetChannel)) continue;
                var msgType = ps[1].ParameterType;
                if (false == typeof(INetMessage).IsAssignableFrom(msgType)) continue;
                var actionType = typeof(Action<,>).MakeGenericType(typeof(NetChannel), msgType);
                var action = Delegate.CreateDelegate(actionType, adapter, method);
                var actionInfo = new ActionInfo
                {
                    msgType = msgType,
                    action = action
                };
                adapter.actionInfos.Add(actionInfo);

                var recvMethod = engine.slave.GetType().GetMethod("Recv").MakeGenericMethod(msgType);
                recvMethod.Invoke(engine.slave, [action]);
            }
        }

        private void NetUnRecv()
        {
            foreach (var actionInfo in adapter.actionInfos)
            {
                var recvMethod = engine.slave.GetType().GetMethod("UnRecv").MakeGenericMethod(actionInfo.msgType);
                recvMethod.Invoke(engine.slave, [actionInfo.action]);
            }
            adapter.actionInfos.Clear();
        }
    }
}
