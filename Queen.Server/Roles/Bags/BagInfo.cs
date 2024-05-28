using Newtonsoft.Json;
using Queen.Server.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Bags
{
    /// <summary>
    /// 背包物品数据
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class BagItem
    {
        /// <summary>
        /// 唯一 ID
        /// </summary>
        [JsonProperty]
        public int id { get; set; }
        /// <summary>
        /// 配置 ID
        /// </summary>
        [JsonProperty]
        public int cfg { get; set; }
        /// <summary>
        /// 数量
        /// </summary>
        [JsonProperty]
        public int count { get; set; }
    }

    /// <summary>
    /// 背包数据
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class BagInfo : IDBState
    {
        /// <summary>
        /// 自增 ID
        /// </summary>
        [JsonProperty]
        public int incrementId { get; set; } = 1000;
        /// <summary>
        /// 背包物品集合
        /// </summary>
        [JsonProperty]
        public List<BagItem> items { get; set; } = new();
    }
}
