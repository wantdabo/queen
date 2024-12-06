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
    /// WS 端口
    /// </summary>
    public ushort wsport { get; private set; }
    /// <summary>
    /// 最大连接数
    /// </summary>
    public int maxconn { get; private set; }
    /// <summary>
    /// Slave（主网）最大工作线程
    /// </summary>
    public int sthread { get; private set; }
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
    /// RPC 主机
    /// </summary>
    public string rpchost { get; private set; }
    /// <summary>
    /// RPC 端口
    /// </summary>
    public ushort rpcport { get; private set; }
    /// <summary>
    /// RPC 闲置等待的数量（库存通信用的 RPC.Clinet）
    /// </summary>
    public ushort rpcidlecc { get; private set; }
    /// <summary>
    /// RPC 超时设定
    /// </summary>
    public uint rpctimeout { get; private set; }
    /// <summary>
    /// RPC 超时延长，目标如果收到 RPC 消息 ACK，将会进入这个等待时长
    /// </summary>
    public uint rpcdeadtime { get; private set; }
    /// <summary>
    /// COMPASS 主机
    /// </summary>
    public string compasshost { get; private set; }
    /// <summary>
    /// COMPASS 端口
    /// </summary>
    public ushort compassport { get; private set; }
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
    /// <summary>
    /// Role 离线销毁时间（秒）
    /// </summary>
    public int roledestroy { get; private set; }
    /// <summary>
    /// Role 任务最大数量
    /// </summary>
    public int roletaskmax { get; private set; }
    /// <summary>
    /// Role 联系最大数量
    /// </summary>
    public int rolecontactmax { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
        name = jobj.Value<string>("name");
        host = jobj.Value<string>("host");
        port = jobj.Value<ushort>("port");
        wsport = jobj.Value<ushort>("wsport");
        maxconn = jobj.Value<int>("maxconn");
        sthread = jobj.Value<int>("sthread");
        maxpps = jobj.Value<int>("maxpps");
        rpchost = jobj.Value<string>("rpchost");
        rpcport = jobj.Value<ushort>("rpcport");
        rpcidlecc = jobj.Value<ushort>("rpcidlecc");
        rpctimeout = jobj.Value<uint>("rpctimeout");
        rpcdeadtime = jobj.Value<uint>("rpcdeadtime");
        compasshost = jobj.Value<string>("compasshost");
        compassport = jobj.Value<ushort>("compassport");
        dbhost = jobj.Value<string>("dbhost");
        dbport = jobj.Value<int>("dbport");
        dbname = jobj.Value<string>("dbname");
        dbauth = jobj.Value<bool>("dbauth");
        dbuser = jobj.Value<string>("dbuser");
        dbpwd = jobj.Value<string>("dbpwd");
        dbsave = jobj.Value<int>("dbsave");
        roledestroy = jobj.Value<int>("roledestroy");
        roletaskmax = jobj.Value<int>("roletaskmax");
        rolecontactmax = jobj.Value<int>("rolecontactmax");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
