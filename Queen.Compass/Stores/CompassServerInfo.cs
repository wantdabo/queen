using Newtonsoft.Json;

namespace Queen.Compass.Stores;

/// <summary>
/// Server 信息
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class CompassServerInfo
{
    /// <summary>
    /// Server 名字
    /// </summary>
    [JsonProperty]
    public string name { get; set; }
    /// <summary>
    /// Server RPC 名字
    /// </summary>
    [JsonProperty]
    public string rpc { get; set; }
    /// <summary>
    /// 角色数量
    /// </summary>
    [JsonProperty]
    public int rolecnt { get; set; }
    /// <summary>
    /// 在线角色数量
    /// </summary>
    [JsonProperty]
    public int onlinerolecnt { get; set; }
    /// <summary>
    /// 主机
    /// </summary>
    [JsonProperty]
    public string host { get; set; }
    /// <summary>
    /// 主机端口
    /// </summary>
    [JsonProperty]
    public ushort port { get; set; }
}