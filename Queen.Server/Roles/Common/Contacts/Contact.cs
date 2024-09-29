using Queen.Compass.Stores;
using Queen.Compass.Stores.Common;
using Queen.Network.Cross;
using Queen.Server.Core;
using System.Reflection;

namespace Queen.Server.Roles.Common.Contacts;

/// <summary>
/// Role 联系
/// </summary>
public class Contact : Comp
{
    /// <summary>
    /// 路由枚举
    /// </summary>
    public const string ROUTE = "SERVER/CONTACT";
    /// <summary>
    /// 玩家自身
    /// </summary>
    private Role role { get; set; }
    /// <summary>
    /// 传呼机
    /// </summary>
    private Pager pager { get; set; }
    /// <summary>
    /// 传呼机 API 方法集合
    /// </summary>
    private Dictionary<string, Delegate> apiactionDict = new();
    
    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="role">玩家自身</param>
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
    
    /// <summary>
    /// 自动注销 API
    /// </summary>
    private void ContactUnRouting()
    {
        apiactionDict.Clear();
    }

    /// <summary>
    /// 自动注册 API
    /// </summary>
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
    
    /// <summary>
    /// 根据 UUID 搜索玩家传呼机
    /// </summary>
    /// <param name="objective">目标玩家 UUID</param>
    /// <param name="pager">传呼机</param>
    /// <returns>YES/NO</returns>
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
    
    /// <summary>
    /// 响应路由分发 API
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    /// <param name="info">API 信息</param>
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
