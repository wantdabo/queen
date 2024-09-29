using Newtonsoft.Json;

namespace Queen.Server.Roles.Common.Contacts;

/// <summary>
/// 联系方式信息
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class ContactInfo
{
    /// <summary>
    /// 目标玩家的 UUID
    /// </summary>
    [JsonProperty]
    public string uuid { get; set; }
    /// <summary>
    /// API 名字
    /// </summary>
    [JsonProperty]
    public string api { get; set; }
    /// <summary>
    /// 信息内容
    /// </summary>
    [JsonProperty]
    public string content { get; set; }
}