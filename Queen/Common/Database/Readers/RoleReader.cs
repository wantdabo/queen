using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Common.Database.Readers
{
    public class RoleReader : DBReader
    {
        public override Type type => GetType();

        public string pid { get; set; }
        public string nickName { get; set; }
        public string username { get; set; }
        public string password { get; set; }
    }
}
