namespace Queen.Common.Extensions;

public static class NewtonJsonExtensions
{
    public static string SSD2Json(this Dictionary<string, string> ssd)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(ssd);
    }

    public static Dictionary<string, string> Json2SSD(this string content)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
    }
}
