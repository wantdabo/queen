using Newtonsoft.Json;

namespace Queen.Compass.Stores;

[JsonObject(MemberSerialization.OptIn)]
public class PointInfo
{
    [JsonProperty]
    public string name { get; set; }
    [JsonProperty]
    public string ip { get; set; }
    [JsonProperty]
    public ushort port { get; set; }
}