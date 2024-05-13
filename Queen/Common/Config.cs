using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Bright.Serialization;
using Newtonsoft.Json.Linq;
using Queen.Core;

namespace Queen.Common
{
    /// <summary>
    /// 游戏配置
    /// </summary>
    public class Config : Comp
    {
        /// <summary>
        /// 配置表定位器
        /// </summary>
        public Tables location;

        /// <summary>
        /// 系数 0.5
        /// </summary>
        public float half = 0.5f;

        /// <summary>
        /// 系数 1
        /// </summary>
        public float one = 1f;

        /// <summary>
        /// 系数 1000
        /// </summary>
        public float thousand = 1000f;

        /// <summary>
        /// 浮点数转整型的乘法系数（10000 表示 1）
        /// </summary>
        public int float2Int = 10000;

        /// <summary>
        /// 整型转浮点的乘法系数（10000 表示 1）
        /// </summary>
        public float int2Float = 0.0001f;

        /// <summary>
        /// 服务器名字
        /// </summary>
        public string name { get; private set; }

        /// <summary>
        /// IP 地址
        /// </summary>
        public string host { get; private set; }

        /// <summary>
        /// 端口
        /// </summary>
        public ushort port { get; private set; }

        /// <summary>
        /// 最大连接数
        /// </summary>
        public int maxconn { get; private set; }

        /// <summary>
        /// 数据库主机
        /// </summary>
        public string dbhost { get; private set; }

        /// <summary>
        /// 数据库名
        /// </summary>
        public string dbname { get; private set; }

        /// <summary>
        /// 数据库用户名
        /// </summary>
        public string dbuser { get; private set; }

        /// <summary>
        /// 数据库密码
        /// </summary>
        public string dbpwd { get; private set; }

        /// <summary>
        /// 数据落地时间间隔（秒）
        /// </summary>
        public int dbsave { get; private set; }

        /// <summary>
        /// 引擎 tick 间隔 (ms)
        /// </summary>
        public int tick { get; private set; }

        /// <summary>
        /// GM 模式关启
        /// </summary>
        public bool gmmode { get; private set; }

        /// <summary>
        /// 资源目录
        /// </summary>
        public string respath { get { return $"{Directory.GetCurrentDirectory()}/Res/"; } }

        /// <summary>
        /// 数据目录
        /// </summary>
        public string datapath { get { return $"{respath}/Datas/"; } }

        /// <summary>
        /// 日志目录
        /// </summary>
        public string logpath { get { return $"{respath}/Logs/"; } }

        /// <summary>
        /// 初始化配置表
        /// </summary>
        /// <returns>Task</returns>
        public void Initial()
        {
            if (false == Directory.Exists(datapath)) Directory.CreateDirectory(datapath);
            if (false == Directory.Exists(logpath)) Directory.CreateDirectory(logpath);

            var jobj = JObject.Parse(File.ReadAllText($"{respath}settings.json"));
            name = jobj.Value<string>("name");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
            maxconn = jobj.Value<int>("maxconn");
            dbhost = jobj.Value<string>("dbhost");
            dbname = jobj.Value<string>("dbname");
            dbuser = jobj.Value<string>("dbuser");
            dbpwd = jobj.Value<string>("dbpwd");
            dbsave = jobj.Value<int>("dbsave");
            tick = jobj.Value<int>("tick");
            gmmode = jobj.Value<bool>("gmmode");

            location = new Tables((cfgName) => { return new ByteBuf(File.ReadAllBytes($"{respath}Configs/{cfgName}.bytes")); });
        }
    }
}