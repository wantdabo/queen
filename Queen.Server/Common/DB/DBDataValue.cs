using MongoDB.Bson.Serialization.Attributes;
using Queen.Common.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Common.DB
{
    /// <summary>
    /// Mongo 对应数据, 游戏数据
    /// </summary>
    public class DBDataValue : DBValue
    {
        /// <summary>
        /// Key
        /// </summary>
        [BsonElement("prefix")]
        public string prefix { get; set; }
        /// <summary>
        /// Value
        /// </summary>
        [BsonElement("value")]
        public string value { get; set; }
    }
}
