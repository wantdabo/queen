using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen
{
    public static class Settings
    {
        /// <summary>
        /// 服务器名字
        /// </summary>
        public static string name { get; private set; }

        /// <summary>
        /// 主机
        /// </summary>
        public static string host { get; private set; }

        /// <summary>
        /// 端口
        /// </summary>
        public static ushort port { get; private set; }

        /// <summary>
        /// 最大连接数
        /// </summary>
        public static int maxconn { get; private set; }

        /// <summary>
        /// 数据库主机
        /// </summary>
        public static string dbhost { get; private set; }

        /// <summary>
        /// 数据库名
        /// </summary>
        public static string dbname { get; private set; }

        /// <summary>
        /// 数据库用户名
        /// </summary>
        public static string dbuser { get; private set; }

        /// <summary>
        /// 数据库密码
        /// </summary>
        public static string dbpwd { get; private set; }

        /// <summary>
        /// 数据落地时间间隔（秒）
        /// </summary>
        public static int dbsave { get; private set; }

        static Settings()
        {
            var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
            name = jobj.Value<string>("name");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
            maxconn = jobj.Value<int>("maxconn");
            dbhost = jobj.Value<string>("dbhost");
            dbname = jobj.Value<string>("dbname");
            dbuser = jobj.Value<string>("dbuser");
            dbpwd = jobj.Value<string>("dbpwd");
            dbsave = jobj.Value<int>("dbsave");
        }
    }
}
