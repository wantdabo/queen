using Newtonsoft.Json.Linq;
using Queen.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server;

public class Settings : Comp
{
    /// <summary>
    /// 服务器名字
    /// </summary>
    public string name { get; private set; }

    /// <summary>
    /// 主机
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
    /// Slave（主网）最大工作线程
    /// </summary>
    public int sthread { get; private set;}

    /// <summary>
    /// 最大网络收发包每秒
    /// </summary>
    public int maxpps { get; private set; }

    /// <summary>
    /// 数据库主机
    /// </summary>
    public string dbhost { get; private set; }

    /// <summary>
    /// 数据库端口
    /// </summary>
    public int dbport { get; private set; }

    /// <summary>
    /// 数据库名
    /// </summary>
    public string dbname { get; private set; }

    /// <summary>
    /// 数据库身份校验
    /// </summary>
    public bool dbauth { get; private set; }

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

    protected override void OnCreate()
    {
            base.OnCreate();
            var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
            name = jobj.Value<string>("name");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
            maxconn = jobj.Value<int>("maxconn");
            sthread = jobj.Value<int>("sthread");
            maxpps = jobj.Value<int>("maxpps");
            dbhost = jobj.Value<string>("dbhost");
            dbport = jobj.Value<int>("dbport");
            dbname = jobj.Value<string>("dbname");
            dbauth = jobj.Value<bool>("dbauth");
            dbuser = jobj.Value<string>("dbuser");
            dbpwd = jobj.Value<string>("dbpwd");
            dbsave = jobj.Value<int>("dbsave");
        }

    protected override void OnDestroy()
    {
            base.OnDestroy();
        }
}