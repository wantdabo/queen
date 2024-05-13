using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Common.Database.Readers
{
    /// <summary>
    /// 玩家数据库信息读取器
    /// </summary>
    public class RoleReader : DBReader
    {
        public override Type type => GetType();
        
        /// <summary>
        /// 玩家 ID
        /// </summary>
        public string pid { get; set; }
        /// <summary>
        /// 玩家昵称
        /// </summary>
        public string nickname { get; set; }
        /// <summary>
        /// 用户名
        /// </summary>
        public string username { get; set; }
        /// <summary>
        /// 密码
        /// </summary>
        public string password { get; set; }
    }
}
