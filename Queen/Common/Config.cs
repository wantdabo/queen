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
        public string hostName { get; private set; }

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
        public int maxConn { get; private set; }

        /// <summary>
        /// 数据库主机
        /// </summary>
        public string dbHost { get; private set; }

        /// <summary>
        /// 数据库名
        /// </summary>
        public string dbName { get; private set; }

        /// <summary>
        /// 数据库用户名
        /// </summary>
        public string dbUser { get; private set; }

        /// <summary>
        /// 数据库密码
        /// </summary>
        public string dbPwd { get; private set; }

        /// <summary>
        /// 数据落地时间间隔（秒）
        /// </summary>
        public int dbSave { get; private set; }

        /// <summary>
        /// 引擎 tick 间隔 (ms)
        /// </summary>
        public int engineTick { get; private set; }

        /// <summary>
        /// GM 模式关启
        /// </summary>
        public bool gmMode { get; private set; }

        /// <summary>
        /// 资源目录
        /// </summary>
        public string resPath { get { return $"{Directory.GetCurrentDirectory()}/Res/"; } }

        /// <summary>
        /// 日志目录
        /// </summary>
        public string logPath { get { return $"{resPath}/Logs/"; } }

        /// <summary>
        /// 初始化配置表
        /// </summary>
        /// <returns>Task</returns>
        public void Initial()
        {
            var jobj = JObject.Parse(File.ReadAllText($"{resPath}settings.json"));
            hostName = jobj.Value<string>("hostname");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
            maxConn = jobj.Value<int>("maxconn");
            dbHost = jobj.Value<string>("dbhost");
            dbName = jobj.Value<string>("dbname");
            dbUser = jobj.Value<string>("dbuser");
            dbPwd = jobj.Value<string>("dbpwd");
            dbSave = jobj.Value<int>("dbsave");
            engineTick = jobj.Value<int>("enginetick");
            gmMode = jobj.Value<bool>("gmmode");

            var path = Directory.GetCurrentDirectory();
            location = new Tables((cfgName) => { return new ByteBuf(File.ReadAllBytes($"{resPath}Configs/{cfgName}.bytes")); });
        }
    }
}