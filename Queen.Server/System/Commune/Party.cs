using Queen.Common;
using Queen.Compass.Stores;
using Queen.Compass.Stores.Common;
using Queen.Core;
using Queen.Network.Common;
using Queen.Network.Cross;
using Queen.Protocols.Common;
using Queen.Server.Core;
using Queen.Server.Roles.Common;
using Queen.Server.Roles.Common.Contacts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;

namespace Queen.Server.System.Commune;

/// <summary>
/// 玩家加入事件
/// </summary>
public struct RoleJoinEvent : IEvent
{
    /// <summary>
    /// 玩家
    /// </summary>
    public Role role { get; set; }
}

/// <summary>
/// 玩家退出事件
/// </summary>
public struct RoleQuitEvent : IEvent
{
    /// <summary>
    /// 玩家
    /// </summary>
    public Role role { get; set; }
}

/// <summary>
/// 玩家加入数据
/// </summary>
public struct RoleJoinInfo
{
    /// <summary>
    /// 通信渠道
    /// </summary>
    public NetChannel channel { get; set; }
    /// <summary>
    /// 玩家 ID
    /// </summary>
    public string uuid { get; set; }
    /// <summary>
    /// 昵称
    /// </summary>
    public string nickname { get; set; }
    /// <summary>
    /// 用户名
    /// </summary>
    public string username { get; set; }
    /// <summary>
    /// 密码
    /// </summary>
    public string password { get; set; }
}

/// <summary>
/// 派对/ 玩家办事处
/// </summary>
public class Party : Sys
{
    /// <summary>
    /// 玩家集合
    /// </summary>
    private Dictionary<string, Role> uuidRoleDict = new();
    /// <summary>
    /// 通信渠道玩家集合
    /// </summary>
    private Dictionary<string, Role> channelRoleDict = new();
    /// <summary>
    /// 已注册的消息类型
    /// </summary>
    private List<Type> regedmsgs = new();
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
        engine.eventor.Listen<ExecuteEvent>(OnExecute);
        engine.ticker.eventor.Listen<TickEvent>(OnTick);
        engine.rpc.Routing(Contact.ROUTE, OnContact);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.eventor.UnListen<ExecuteEvent>(OnExecute);
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
            uuidRoleDict.Add(role.info.uuid, role);
        }
        role.session.channel = info.channel;
        channelRoleDict.AddOrUpdate(info.channel.id, role);

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
    /// 注销消息接收
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="action">回调</param>
    public void Recv<T>(Action<T> action) where T : INetMessage
    {
        if (regedmsgs.Contains(typeof(T))) return;
        regedmsgs.Add(typeof(T));

        engine.slave.Recv((NetChannel c, T msg) =>
        {
            if (false == c.alive) return;
            var role = GetRole(c);
            if (null == role || false == role.session.channel.alive) return;
            role.OnRecv(msg);
        });
    }

    /// <summary>
    /// 获取玩家
    /// </summary>
    /// <param name="uuid">玩家 ID</param>
    /// <returns>玩家</returns>
    public Role GetRole(string uuid)
    {
        uuidRoleDict.TryGetValue(uuid, out var role);

        return role;
    }

    /// <summary>
    /// 获取玩家
    /// </summary>
    /// <param name="channel">通信渠道</param>
    /// <returns>玩家</returns>
    public Role GetRole(NetChannel channel)
    {
        return channelRoleDict.GetValueOrDefault(channel.id);
    }

    /// <summary>
    /// 计数
    /// </summary>
    private void Counter()
    {
        cnt = uuidRoleDict.Count;
        int onlinecnt = 0;
        foreach (var kv in uuidRoleDict) if (kv.Value.online) onlinecnt++;
        this.onlinecnt = onlinecnt;
    }
    
    private void OnExecute(ExecuteEvent e)
    {
        Parallel.ForEach(uuidRoleDict, kv =>
        {
            if (kv.Value.working) return;
            Task.Run(kv.Value.Work);
        });
    }

    private void OnTick(TickEvent e)
    {
        Counter();
        foreach (var kv in uuidRoleDict)
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
            if (role.online) continue;

            uuidRoleDict.Remove(kv.Key);
            role.Destroy();
            engine.rpc.CrossAsync(engine.settings.compasshost, engine.settings.compassport, CompassRouteDef.DEL_ROLE, kv.Key);
        }
        foreach (string key in deleteElapsedCaches)
            if (offlineElapsedDict.ContainsKey(key))
                offlineElapsedDict.Remove(key);
    }

    private void OnContact(CrossContext context)
    {
        var contact = Newtonsoft.Json.JsonConvert.DeserializeObject<ContactInfo>(context.content);
        var role = GetRole(contact.uuid);
        if (null == role)
        {
            context.Response(CROSS_STATE.ERROR, "ROLE_NOT_FOUND");
            return;
        }

        role.Contact(context, contact);
    }
}
