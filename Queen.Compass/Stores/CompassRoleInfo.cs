using Newtonsoft.Json;

namespace Queen.Compass.Stores;

[JsonObject(MemberSerialization.OptIn)]
public class CompassRoleInfo
{
    [JsonProperty]
    public string uuid { get; set; }
    [JsonProperty]
    public bool online { get; set; }
    [JsonProperty]
    public string rpc { get; set; }
}
