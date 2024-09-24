using Newtonsoft.Json;

namespace Queen.Compass.Stores;

[JsonObject(MemberSerialization.OptIn)]
public class RoleInfo
{
    [JsonProperty]
    public string point { get; set; }
    [JsonProperty]
    public string uuid { get; set; }
}
