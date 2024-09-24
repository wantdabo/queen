using Newtonsoft.Json;

namespace Queen.Compass.Stores;

[JsonObject(MemberSerialization.OptIn)]
public class RoleInfo
{
    [JsonProperty]
    public string uuid { get; set; }
    [JsonProperty]
    public string point { get; set; }
    [JsonProperty]
    public string ip { get; set; }
    [JsonProperty]
    public ushort port { get; set; }
}
