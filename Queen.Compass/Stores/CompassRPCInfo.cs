using Newtonsoft.Json;

namespace Queen.Compass.Stores;

[JsonObject(MemberSerialization.OptIn)]
public class CompassRPCInfo
{
    [JsonProperty]
    public string name { get; set; }
    [JsonProperty]
    public string host { get; set; }
    [JsonProperty]
    public ushort port { get; set; }
}