using Queen.Compass.Stores;
using Queen.Compass.Stores.Common;
using Queen.Network.Cross;
using Queen.Server.Core;
using System.Reflection;

namespace Queen.Server.Roles.Common.Contacts;

public class Contact : Comp
{
    public const string ROUTE = "SERVER/CONTACT";
    private Role role { get; set; }
    private Pager pager { get; set; }

    private Dictionary<string, Delegate> apiactionDict = new();

    public void Initialize(Role role)
    {
        this.role = role;
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        pager = AddComp<Pager>();
        pager.Initialize(role);
        pager.Create();
        ContactRouting();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        ContactUnRouting();
    }

    private void ContactUnRouting()
    {
        apiactionDict.Clear();
    }

    private void ContactRouting()
    {
        foreach (var method in pager.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = method.GetCustomAttribute<RoleContact>();
            if (null == attribute) continue;
            var action = Delegate.CreateDelegate(typeof(Action<CrossContext, ContactInfo>), pager, method);
            apiactionDict.Add(attribute.api, action);
        }
    }

    public bool Search(string objective, out Pager pager)
    {
        pager = default;

        if (objective.Equals(role.info.uuid)) return false;
        
        var result = engine.rpc.CrossSync(engine.settings.compasshost, engine.settings.compassport, CompassRouteDef.GET_ROLE, objective);
        if (CROSS_STATE.SUCCESS != result.state) return false;

        var compassrole = Newtonsoft.Json.JsonConvert.DeserializeObject<CompassRoleInfo>(result.content);
        result = engine.rpc.CrossSync(engine.settings.compasshost, engine.settings.compassport, CompassRouteDef.GET_RPC, compassrole.rpc);
        if (CROSS_STATE.SUCCESS != result.state) return false;
        
        var compassrpc = Newtonsoft.Json.JsonConvert.DeserializeObject<CompassRPCInfo>(result.content);
        this.pager.ChangeObjective(objective, compassrpc.host, compassrpc.port);
        pager = this.pager;

        return true;
    }

    public void OnContact(CrossContext context, ContactInfo info)
    {
        if (false == apiactionDict.TryGetValue(info.api, out var action))
        {
            context.Response(CROSS_STATE.ERROR, "API_NOT_FOUND");
            return;
        }
        
        action.DynamicInvoke(context, info);
    }
}
