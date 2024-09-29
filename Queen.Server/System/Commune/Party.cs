using Queen.Common;
using Queen.Compass.Stores;
using Queen.Compass.Stores.Common;
using Queen.Core;
using Queen.Network.Common;
using Queen.Network.Cross;
using Queen.Server.Core;
using Queen.Server.Roles.Common;
using Queen.Server.Roles.Common.Contacts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.System.Commune;

/// <summary>
/// 玩家加入事件
/// </summary>
public struct RoleJoinEvent : IEvent
{
    /// <summary>
    /// 玩家
    /// </summary>
    public Role role;
}

/// <summary>
/// 玩家退出事件
/// </summary>
public struct RoleQuitEvent : IEvent
{
    /// <summary>
    /// 玩家
    /// </summary>
    public Role role;
}

/// <summary>
/// 玩家加入数据
/// </summary>
public struct RoleJoinInfo
{
    /// <summary>
    /// 通信渠道
    /// </summary>
    public NetChannel channel;
    /// <summary>
    /// 玩家 ID
    /// </summary>
    public string uuid;
    /// <summary>
    /// 昵称
    /// </summary>
    public string nickname;
    /// <summary>
    /// 用户名
    /// </summary>
    public string username;
    /// <summary>
    /// 密码
    /// </summary>
    public string password;
}

/// <summary>
/// 派对/ 玩家办事处
/// </summary>
public class Party : Sys
{
    /// <summary>
    /// 玩家集合
    /// </summary>
    private Dictionary<string, Role> roleDict = new();
    /// <summary>
    /// 离线时间记录
    /// </summary>
    private Dictionary<string, float> offlineElapsedDict = new();
    /// <summary>
    /// 离线时间记录缓存
    /// </summary>
    private List<string> deleteElapsedCaches = new();
    /// <summary>
    /// 玩家数量
    /// </summary>
    public int cnt { get; private set; }
    /// <summary>
    /// 在线玩家数量
    /// </summary>
    public int onlinecnt { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        engine.ticker.eventor.Listen<TickEvent>(OnTick);
        engine.rpc.Routing(Contact.ROUTE, OnContact);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.ticker.eventor.UnListen<TickEvent>(OnTick);
        engine.rpc.UnRouting(Contact.ROUTE, OnContact);
    }

    /// <summary>
    /// 玩家加入
    /// </summary>
    /// <param name="info">玩家加入数据</param>
    /// <returns>玩家</returns>
    public void Join(RoleJoinInfo info)
    {
        var role = GetRole(info.uuid);
        if (null != role && role.online) Quit(role);

        if (null == role)
        {
            role = AddComp<Role>();
            role.session = role.AddComp<Session>();
            role.session.Create();
            role.Initialize(new() { uuid = info.uuid, username = info.username, nickname = info.nickname, password = info.password });
            role.Create();
            roleDict.Add(role.info.uuid, role);
        }
        role.session.channel = info.channel;

        engine.eventor.Tell(new RoleJoinEvent { role = role });
        engine.rpc.CrossAsync(engine.settings.compasshost, engine.settings.compassport, CompassRouteDef.SET_ROLE, new CompassRoleInfo
        {
            uuid = role.info.uuid,
            online = true,
            rpc = engine.settings.name,
        });
    }

    /// <summary>
    /// 玩家退出
    /// </summary>
    /// <param name="role">玩家</param>
    public void Quit(Role role)
    {
        if (false == role.online) return;
        
        role.session.channel.Disconnect();
        role.session.channel = null;

        engine.eventor.Tell(new RoleQuitEvent { role = role });
        engine.rpc.CrossAsync(engine.settings.compasshost, engine.settings.compassport, CompassRouteDef.SET_ROLE, new CompassRoleInfo
        {
            uuid = role.info.uuid,
            online = false,
            rpc = engine.settings.name,
        });
    }

    /// <summary>
    /// 获取玩家
    /// </summary>
    /// <param name="uuid">玩家 ID</param>
    /// <returns>玩家</returns>
    public Role GetRole(string uuid)
    {
        roleDict.TryGetValue(uuid, out var role);

        return role;
    }

    /// <summary>
    /// 获取玩家
    /// </summary>
    /// <param name="channel">通信渠道</param>
    /// <returns>玩家</returns>
    public Role GetRole(NetChannel channel)
    {
        foreach (var kv in roleDict)
        {
            if (null == kv.Value.session.channel) continue;
            if (channel.id == kv.Value.session.channel.id) return kv.Value;
        }

        return default;
    }

    /// <summary>
    /// 计数
    /// </summary>
    private void Counter()
    {
        cnt = roleDict.Count;
        int onlinecnt = 0;
        foreach (var kv in roleDict) if (kv.Value.online) onlinecnt++;
        this.onlinecnt = onlinecnt;
    }

    private void OnTick(TickEvent e)
    {
        Counter();
        foreach (var kv in roleDict)
        {
            if (false == kv.Value.online)
            {
                offlineElapsedDict.Remove(kv.Key, out var elapsed);
                elapsed += e.tick;
                offlineElapsedDict.Add(kv.Key, elapsed);
                continue;
            }
            
            if (offlineElapsedDict.ContainsKey(kv.Key)) offlineElapsedDict.Remove(kv.Key);
        }
        
        deleteElapsedCaches.Clear();
        foreach (var kv in offlineElapsedDict)
        {
            if (kv.Value < engine.settings.roledestroy) continue;
            if (false == deleteElapsedCaches.Contains(kv.Key)) deleteElapsedCaches.Add(kv.Key);
            
            var role = GetRole(kv.Key);
            if (null == role) continue;
            if(role.online) continue;
            
            roleDict.Remove(kv.Key);
            role.Destroy();
            engine.rpc.CrossAsync(engine.settings.compasshost, engine.settings.compassport, CompassRouteDef.DEL_ROLE, kv.Key);
        }
        foreach (string key in deleteElapsedCaches) if (offlineElapsedDict.ContainsKey(key)) offlineElapsedDict.Remove(key);
    }
    
    private void OnContact(CrossContext context)
    {
        var contact = Newtonsoft.Json.JsonConvert.DeserializeObject<ContactInfo>(context.content);
        var role  = GetRole(contact.uuid);
        if (null == role)
        {
            context.Response(CROSS_STATE.ERROR, "ROLE_NOT_FOUND");
            return;
        }

        role.OnContact(context, contact);
    }
}
