using Newtonsoft.Json;

namespace Queen.Compass.Stores;

/// <summary>
/// RPC 信息
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class CompassRPCInfo
{
    /// <summary>
    /// RPC 名字
    /// </summary>
    [JsonProperty]
    public string name { get; set; }
    /// <summary>
    /// RPC 主机
    /// </summary>
    [JsonProperty]
    public string host { get; set; }
    /// <summary>
    /// RPC 端口
    /// </summary>
    [JsonProperty]
    public ushort port { get; set; }
}