using MongoDB.Driver;
using Queen.Common;
using Queen.Common.DB;
using Queen.Network.Common;
using Queen.Protocols;
using Queen.Protocols.Common;
using Queen.Server.Core;
using Queen.Server.Roles.Bags;
using Queen.Server.System.Commune;
using System.Collections.Concurrent;

namespace Queen.Server.Roles.Common;

/// <summary>
/// 数据保存事件
/// </summary>
public struct DBSaveEvent : IEvent
{
}

#region 玩家数据结构
/// <summary>
/// 玩家数据结构
/// </summary>
public struct RoleInfo
{
    /// <summary>
    /// 玩家 ID
    /// </summary>
    public string uuid { get; set; }
    /// <summary>
    /// 用户名
    /// </summary>
    public string username { get; set; }
    /// <summary>
    /// 昵称
    /// </summary>
    public string nickname { get; set; }
    /// <summary>
    /// 密码
    /// </summary>
    public string password { get; set; }

    /// <summary>
    /// 比较
    /// </summary>
    /// <param name="other">另一个 RoleInfo</param>
    /// <returns>YES/NO</returns>
    public bool Equals(RoleInfo other)
    {
        if (false == uuid.Equals(other.uuid)) return false;
        if (false == username.Equals(other.username)) return false;
        if (false == nickname.Equals(other.nickname)) return false;
        if (false == password.Equals(other.password)) return false;

        return true;
    }

    public static bool operator !=(RoleInfo r0, RoleInfo r1)
    {
        return false == r0.Equals(r1);
    }

    public static bool operator ==(RoleInfo r0, RoleInfo r1)
    {
        return r0.Equals(r1);
    }
}
#endregion

/// <summary>
/// 玩家
/// </summary>
public class Role : Comp
{
    /// <summary>
    /// 在线
    /// </summary>
    public bool online { get; private set; }

    /// <summary>
    /// 玩家信息
    /// </summary>
    public RoleInfo info { get; private set; }
    /// <summary>
    /// 玩家信息 (数据库)
    /// </summary>
    private RoleInfo dbcache { get; set; }

    /// <summary>
    /// 玩家信息备份
    /// </summary>
    private RoleInfo backup { get; set; }

    /// <summary>
    /// 玩家会话
    /// </summary>
    public Session session { get; set; }

    /// <summary>
    /// 事件订阅派发者
    /// </summary>
    public Eventor eventor { get; set; }

    /// <summary>
    /// 工作中
    /// </summary>
    public bool working { get; private set; }

    /// <summary>
    /// 任务列表
    /// </summary>
    private ConcurrentQueue<Action> jobs = new();

    /// <summary>
    /// 发送列表
    /// </summary>
    private ConcurrentQueue<Action> sends = new();

    /// <summary>
    /// behaviors 集合
    /// </summary>
    private Dictionary<Type, RoleBehavior> behaviorDict = new();

    /// <summary>
    /// 协议映射集合
    /// </summary>
    private Dictionary<Delegate, Delegate> actionDict = new();

    /// <summary>
    /// 心跳计时器 ID
    /// </summary>
    private uint heartbeatTiming { get; set; }

    /// <summary>
    /// 数据自动保存计时器 ID
    /// </summary>
    private uint dbsaveTiming { get; set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        eventor = AddComp<Eventor>();
        eventor.Create();
        engine.eventor.Listen<Queen.Core.ExecuteEvent>(OnExecute);
        engine.eventor.Listen<RoleJoinEvent>(OnRoleJoin);
        engine.eventor.Listen<RoleQuitEvent>(OnRoleQuit);
        eventor.Listen<DBSaveEvent>(OnDBSave);
        // 心跳发送
        heartbeatTiming = engine.ticker.Timing((t) =>
        {
            Heartbeat();
        }, 5, -1);
        
        // 数据写盘
        dbsaveTiming = engine.ticker.Timing((t) => TODO(() => { eventor.Tell<DBSaveEvent>(); }), engine.settings.dbsave, -1);

        Behaviors();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.eventor.UnListen<Queen.Core.ExecuteEvent>(OnExecute);
        engine.eventor.UnListen<RoleJoinEvent>(OnRoleJoin);
        engine.eventor.UnListen<RoleQuitEvent>(OnRoleQuit);
        eventor.UnListen<DBSaveEvent>(OnDBSave);
        engine.ticker.StopTimer(dbsaveTiming);
        jobs.Clear();
    }

    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="info">RoleInfo (玩家信息)</param>
    public void Initialize(RoleInfo info)
    {
        this.info = info;
        this.dbcache = info;
    }

    private void Behaviors()
    {
        // 背包
        AddBehavior<Bag>().Create();
    }

    /// <summary>
    /// 获取 Behavior
    /// </summary>
    /// <typeparam name="T">Behavior 类型</typeparam>
    /// <returns>Behavior 实例</returns>
    public T GetBehavior<T>() where T : RoleBehavior
    {
        if (false == behaviorDict.TryGetValue(typeof(T), out var behavior)) return null;

        return behavior as T;
    }

    /// <summary>
    /// 添加 Behavior
    /// </summary>
    /// <typeparam name="T">Behavior 类型</typeparam>
    /// <returns>Behavior 实例</returns>
    /// <exception cref="Exception">不能添加重复的 Behavior</exception>
    public T AddBehavior<T>() where T : RoleBehavior, new()
    {
        if (behaviorDict.ContainsKey(typeof(T))) throw new Exception("can't add repeat behavior.");

        T behavior = AddComp<T>();
        behavior.role = this;
        behaviorDict.Add(typeof(T), behavior);

        return behavior;
    }

    /// <summary>
    /// 注销协议接收
    /// </summary>
    /// <typeparam name="T">协议类型</typeparam>
    /// <param name="action">协议回调</param>w
    public void UnRecv<T>(Action<T> action) where T : INetMessage
    {
        if (actionDict.TryGetValue(action, out var callback))
        {
            engine.slave.UnRecv(callback as Action<NetChannel, T>);
            actionDict.Remove(action);
        }
    }

    /// <summary>
    /// 注册协议接收
    /// </summary>
    /// <typeparam name="T">协议类型</typeparam>
    /// <param name="action">协议回调</param>
    public void Recv<T>(Action<T> action) where T : INetMessage
    {
        Action<NetChannel, T> callback = (c, m) =>
        {
            if (false == c.alive || false == session.channel.alive) return;

            if (c.id == session.channel.id) TODO(() => { action?.Invoke(m); });
        };
        engine.slave.Recv(callback);
        actionDict.Add(action, callback);
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="msg">数据</param>
    public void Send<T>(T msg) where T : INetMessage
    {
        sends.Enqueue(() => { session.channel.Send(msg); });
    }
        
    /// <summary>
    /// 心跳
    /// </summary>
    private void Heartbeat()
    {
        Send(new S2CHeartbeatMsg { timestamp = DateTimeOffset.Now.ToUnixTimeSeconds() });
    }

    /// <summary>
    /// 重置备份
    /// </summary>
    private void Reset()
    {
        foreach (var behavior in behaviorDict.Values) behavior.Reset();
    }

    /// <summary>
    /// 恢复数据
    /// </summary>
    private void Restore()
    {
        if (false == info.Equals(backup)) info = new() { uuid = backup.uuid, username = backup.username, nickname = backup.nickname, password = backup.password };
        foreach (var behavior in behaviorDict.Values) behavior.Restore();
    }

    /// <summary>
    /// 备份数据
    /// </summary>
    private void Backup()
    {
        if (false == info.Equals(backup)) backup = new() { uuid = info.uuid, username = info.username, nickname = info.nickname, password = info.password };
    }

    /// <summary>
    /// 插入任务队列
    /// </summary>
    /// <param name="job">任务</param>
    private void TODO(Action job)
    {
        if (false == online) return;
        if (null == job) return;

        jobs.Enqueue(job);
    }

    /// <summary>
    /// 进行工作
    /// </summary>
    private void Work()
    {
        if (working) return;

        if (0 == jobs.Count) return;
            
        // 开启任务线程
        Task.Run(() =>
        {
            try
            {
                working = true;
                while (true)
                {
                    if (false == jobs.TryDequeue(out var job)) break;
                        
                    // 备份数据
                    Backup();
                    // 执行任务
                    job.Invoke();
                    // 重置备份
                    Reset();
                        
                    // 任务中产生的有效数据，推送至客户端
                    while (sends.TryDequeue(out var send)) send.Invoke();
                }
            }
            catch (Exception e)
            {
                // 任务失败，恢复数据
                Restore();
                // 任务失败，清除任务中的推送消息
                sends.Clear();
                // 任务失败，输出日志
                engine.logger.Error($"role erro, uuid -> {info.uuid}", e);
            }
            finally
            {
                working = false;
            }
        });
    }

    private void OnExecute(Queen.Core.ExecuteEvent e)
    {
        TODO(() => eventor.Tell(e));
        Work();
    }

    private void OnRoleJoin(RoleJoinEvent e)
    {
        if (e.role.info.uuid != info.uuid) return;
        Heartbeat();
        online = true;
        jobs.Clear();
        TODO(() =>
        {
            eventor.Tell(e);
            Send(new S2CRoleJoinedMsg { });
        });
    }

    private void OnRoleQuit(RoleQuitEvent e)
    {
        if (e.role.info.uuid != info.uuid) return;
            
        jobs.Clear();
        TODO(() =>
        {
            eventor.Tell(e);
            eventor.Tell<DBSaveEvent>();
        });
        online = false;
    }

    private void OnDBSave(DBSaveEvent e)
    {
        if (dbcache.Equals(info)) return;

        if (engine.dbo.Replace("roles", Builders<DBRoleValue>.Filter.Eq(p => p.uuid, info.uuid), new() { uuid = info.uuid, nickname = info.nickname, username = info.username, password = info.password }))
        {
            dbcache = info;
        }
    }
}