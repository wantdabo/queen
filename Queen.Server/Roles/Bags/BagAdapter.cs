using Queen.Network;
using Queen.Network.Common;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Bags;

/// <summary>
/// 背包消息适配器
/// </summary>
public class BagAdapter : Adapter<Bag>
{
    [NetBinding]
    private void C2STest(C2STestMsg msg)
    {
        engine.logger.Info(msg.text);
        bridge.role.Send(new S2CTestMsg { text = "Hello, World!" });
    }
}