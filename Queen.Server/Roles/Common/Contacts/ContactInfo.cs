using Newtonsoft.Json;

namespace Queen.Server.Roles.Common.Contacts;

[JsonObject(MemberSerialization.OptIn)]
public class ContactInfo
{
    [JsonProperty]
    public string uuid { get; set; }
    [JsonProperty]
    public string api { get; set; }
    [JsonProperty]
    public string content { get; set; }
}