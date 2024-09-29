using Queen.Network.Cross;
using Queen.Server.Core;

namespace Queen.Server.Roles.Common.Contacts;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RoleContact : Attribute
{
    public string api { get; private set; }
    
    public RoleContact(string api)
    {
        this.api = api;
    }
}

/// <summary>
/// 传呼机, Role 与 Role 之间的沟通
/// </summary>
public class Pager : Comp
{
    public Role role { get; private set; }
    public string objective { get; private set; }
    public string host { get; private set; }
    public ushort port { get; private set; }

    public void Initialize(Role role)
    {
        this.role = role;
    }

    public void ChangeObjective(string objective, string host, ushort port)
    {
        this.objective = objective;
        this.host = host;
        this.port = port;
    }

    private CrossResult Speak(string api, string content)
    {
        return engine.rpc.CrossSync(host, port, Contact.ROUTE, new ContactInfo{uuid = objective, api = api, content = content});
    }

    private CrossResult Speak<T>(string api, T content) where T : class
    {
        return Speak(api, Newtonsoft.Json.JsonConvert.SerializeObject(content));
    }

    public void Test()
    {
        var result = Speak(PagerApiDef.TEST, "Hello, World!");
        engine.logger.Info($"{result.state}, {result.content}");
    }

    [RoleContact(PagerApiDef.TEST)]
    private void OnTest(CrossContext context, ContactInfo info)
    {
        engine.logger.Info(info.content);
        context.Response(CROSS_STATE.SUCCESS, "OK");
    }
}
