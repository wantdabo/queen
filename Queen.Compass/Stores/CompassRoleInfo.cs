using Newtonsoft.Json;

namespace Queen.Compass.Stores;

/// <summary>
/// Role 信息
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class CompassRoleInfo
{
    /// <summary>
    /// UUID
    /// </summary>
    [JsonProperty]
    public string uuid { get; set; }
    /// <summary>
    /// 在线状态
    /// </summary>
    [JsonProperty]
    public bool online { get; set; }
    /// <summary>
    /// RPC 名字
    /// </summary>
    [JsonProperty]
    public string rpc { get; set; }
}
