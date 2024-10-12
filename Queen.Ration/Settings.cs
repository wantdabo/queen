using Newtonsoft.Json.Linq;
using Queen.Ration.Core;

namespace Queen.Ration;

public class Settings : Comp
{
    /// <summary>
    /// 服务器名字
    /// </summary>
    public string name { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
        name = jobj.Value<string>("name");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
