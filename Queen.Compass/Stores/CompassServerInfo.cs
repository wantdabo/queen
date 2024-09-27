using Newtonsoft.Json;

namespace Queen.Compass.Stores;

[JsonObject(MemberSerialization.OptIn)]
public class CompassServerInfo
{
    [JsonProperty]
    public string name { get; set; }
    [JsonProperty]
    public string rpc { get; set; }
    [JsonProperty]
    public int rolecnt { get; set; }
    [JsonProperty]
    public int onlinerolecnt { get; set; }
    [JsonProperty]
    public string host { get; set; }
    [JsonProperty]
    public ushort port { get; set; }
}