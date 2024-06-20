using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Common.DB
{
    /// <summary>
    /// Mongo 对应数据
    /// </summary>
    public abstract class DBValue
    {
        /// <summary>
        /// MongoDB 文档主键
        /// </summary>
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        private ObjectId Id { get; set; }
    }
}
