using Queen.Network.Cross;
using Queen.Server.Core;

namespace Queen.Server.Roles.Common.Contacts;

/// <summary>
/// 传呼机响应方法特性
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RoleContact : Attribute
{
    /// <summary>
    /// API 名字
    /// </summary>
    public string api { get; private set; }
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="api">API 名字</param>
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
    /// <summary>
    /// 玩家自身
    /// </summary>
    public Role role { get; private set; }
    /// <summary>
    /// 目标玩家的 UUID
    /// </summary>
    public string objective { get; private set; }
    /// <summary>
    /// 目标玩家的 RPC 主机
    /// </summary>
    public string host { get; private set; }
    /// <summary>
    /// 目标玩家的 RPC 端口
    /// </summary>
    public ushort port { get; private set; }
    
    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="role">玩家自身</param>
    public void Initialize(Role role)
    {
        this.role = role;
    }
    
    /// <summary>
    /// 切换目标
    /// </summary>
    /// <param name="objective">目标玩家 UUID</param>
    /// <param name="host">目标玩家的 RPC 主机</param>
    /// <param name="port">目标玩家的 RPC 端口</param>
    public void ChangeObjective(string objective, string host, ushort port)
    {
        this.objective = objective;
        this.host = host;
        this.port = port;
    }
    
    /// <summary>
    /// 发送信息
    /// </summary>
    /// <param name="api">API 名字</param>
    /// <param name="content">信息内容</param>
    /// <returns>RPC 结果</returns>
    private CrossResult Speak(string api, string content)
    {
        return engine.rpc.CrossSync(host, port, Contact.ROUTE, new ContactInfo{uuid = objective, api = api, content = content});
    }

    /// <summary>
    /// 发送信息
    /// </summary>
    /// <param name="api">API 名字</param>
    /// <param name="content">信息内容</param>
    /// <typeparam name="T">NewtonJson 实例</typeparam>
    /// <returns>RPC 结果</returns>
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
