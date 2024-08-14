using Queen.Network;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Bags
{
    /// <summary>
    /// 背包消息适配器
    /// </summary>
    public class Adapter : Adapter<Bag>
    {
        protected override void OnBind()
        {
            bridge.role.Recv<C2STestMsg>(OnC2STest);
        }

        protected override void OnUnbind()
        {
            bridge.role.UnRecv<C2STestMsg>(OnC2STest);
        }

        private void OnC2STest(C2STestMsg msg)
        {
            engine.logger.Info($"OnC2STest -> {msg.content}");
        }
    }
}
