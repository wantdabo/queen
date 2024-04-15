using MessagePack;
using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Protocols
{
    [MessagePackObject(true)]
    public class ReqTestMsg : INetMessage
    {
        [Key(0)]
        public int test { get; set; }

        [Key(1)]
        public int test2 { get; set; }
    }

    [MessagePackObject(true)]
    public class ReqTest2Msg : INetMessage
    {
        [Key(0)]
        public int test { get; set; }

        [Key(1)]
        public int test2 { get; set; }
    }
}
