using Newtonsoft.Json;
using Queen.Logic.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Player
{
    [JsonObject(MemberSerialization.OptIn)]
    public class BagItem 
    {
    
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class BagInfo : IDBState
    {
        public int incrementId { get; set; } = 1000;
    }
}
