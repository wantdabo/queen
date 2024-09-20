using Newtonsoft.Json.Linq;
using Queen.Compass.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Compass;

public class Settings : Comp
{
    /// <summary>
    /// 服务器名字
    /// </summary>
    public string name { get; private set; }
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

    protected override void OnCreate()
    {
        base.OnCreate();
        var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
        name = jobj.Value<string>("name");
        rpchost = jobj.Value<string>("rpchost");
        rpcport = jobj.Value<ushort>("rpcport");
        rpcidlecc = jobj.Value<ushort>("rpcidlecc");
        rpctimeout = jobj.Value<uint>("rpctimeout");
        rpcdeadtime = jobj.Value<uint>("rpcdeadtime");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
