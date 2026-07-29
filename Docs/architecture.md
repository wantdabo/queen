# Queen 游戏服务端架构方案

## 核心哲学

**多进程，单线程。IO 异步，业务同步。数据逻辑分离，容器自带脏标记。**

### 核心原则

| 原则 | 说明 |
|------|------|
| 一进程一线程 | 每个进程只有一个主线程执行业务逻辑，无锁 |
| IO 异步 offload | 网络收发、DB 读写走 OS 线程池，结果通过 MPSC 队列回主线程 |
| 业务逻辑同步 | 所有业务方法为 `void` 或返回纯值，主线程执行 |
| 协程处理多帧逻辑 | 需跨帧等待时用 `IEnumerator` 协程，主线程驱动 |
| **Behavior/BehaviorInfo 分离** | Behavior = System (单例逻辑); BehaviorInfo = Component (纯数据) |
| **容器自带脏标记** | GBLList/GBLDict 操作自动记录; `[SyncField]` SourceGen 生成 |
| **零分配** | 核心路径零 GC; 对象池 + `Span<T>` + `ArrayPool` |

### IO / 业务边界（硬约束）

```
                    ┌──────────────────────────────────────┐
                    │         IO 层 (允许 async/Task)       │
                    │  Queen.Network / Queen.Persistence   │
                    └──────────────┬───────────────────────┘
                                   │ MPSC Queue (唯一跨线程接触点)
                    ┌──────────────▼───────────────────────┐
                    │      业务层 (禁止 async/Task)         │
                    │  Queen.Core / 所有 Service           │
                    │  Behavior 方法 → void                │
                    │  IEnumerator Coroutine               │
                    └──────────────────────────────────────┘
```

CI 中 Roslyn Analyzer (`QN1001`) 拦截业务层任何 `async` 方法。

---

## 一、进程拓扑

```
                         CLIENT
            TCP / UDP(KCP) / WebSocket
                          │
                          ▼
   ┌──────────────────────────────────────────────┐
   │              QUEEN.GATEWAY (N 实例)            │
   │  连接管理 · 认证 · session · resumeToken       │
   │  安全: Rate Limit · Token 校验               │
   └──────────────────────┬───────────────────────┘
                          │
                          ▼
   ┌──────────────────────────────────────────────┐
   │              QUEEN.ROUTER (2~N 实例, HA)       │
   │  DNS 模式: 只回答"X 在哪", 不转发             │
   │  Redis Sentinel/Cluster 存储路由数据          │
   └──────────────────────┬───────────────────────┘
                          │
          ★ 全直连 (寻址结果本地缓存 TTL 30s)
   ┌──────────────────────┼───────────────────────┐
   │                      │                       │
   ▼          ▼           ▼           ▼           ▼
player      player      club        chat        rank
.serv N     .serv N     .service N  .service N  .service 1
   │          │           │           │           │
   └──────────┴───────────┴───────────┴───────────┘
   │                      │
   ▼                      ▼
trade.serv 1      auction.svc 1        Ration (HTTP 管理)
   │
   ▼ Batch Write
MongoDB + Redis + WAL

                    ┌─────────────────────┐
                    │  QUEEN.CONTROLLER   │
                    │  (1 实例, 主备)      │
                    │  自动扩缩容          │
                    └─────────────────────┘
```

**Router 管寻址，Controller 管扩缩容，职责分离。所有 Service 之间直连通信。**

---

## 二、Router — DNS 模式

### 2.1 数据模型

```
  Redis (Sentinel/Cluster, HA):
    services:{type}          → Set<instanceAddr>
    services:{type}:hashring → SortedSet<slot, instanceAddr>
    online:{playerId}        → {servAddr, gatewayAddr}  (TTL 5s, player.serv 心跳续期)

  Router 本地缓存: 500ms 刷新
  Gateway/Service 本地缓存: 30s TTL
```

### 2.2 寻址

```
  Router.Lookup(entityId, serviceType):

    if serviceType == "player":
      查 online:{entityId} → 命中 → {serv, gateway}
                           未命中 → "offline"
    else:
      查 hashring → 实例地址 (club: clubId hash, chat: roomId hash, ...)
```

### 2.3 缓存失效与迁移重定向

```
  Role 迁移后 Gateway 可能缓存指向旧 serv:

  Gateway → 旧 serv → 返回 Redirect {newServ}
  Gateway 更新缓存 → 重试到新 serv
```

### 2.4 Redis 高可用

```
  Sentinel 主从 + 自动故障转移
  完全不可用时: Router 从本地缓存应答 (30s 内可用)
  新路由不可用 → 返回 "服务暂不可用"
```

### 2.5 压力分析

```
  Router 只做注册 (N 次/秒) + 寻址 (本地缓存后 ~3 万 QPS)
  Redis 单机 10 万+ QPS → 足够
  高流量走直连, 不经过 Router
```

---

## 三、统一 Service 骨架

**所有业务进程共用一个骨架。player.serv 不特殊。**

### 3.1 术语

| 概念 | 是什么 | 数量 |
|------|--------|------|
| **BehaviorInfo** | Component，纯数据，`[SyncField]` 标记 | 每实体每类型一份 |
| **Behavior** | System，单例逻辑，对应一种 BehaviorInfo | 每种 BehaviorInfo 一个 |
| **DataStore** | 双索引存储，`Get<T>` 懒加载 | 每个 Service 一个 |
| **GBLList / GBLDict** | 带脏标记的集合容器，Add/Remove/Update 自动记录 | — |
| **Entity** | 数据归属单元 | player.serv=Role, club=Club, chat=Room, auction=Listing |

### 3.2 BehaviorInfo (Component)

```csharp
public partial class BagBehaviorInfo
{
    [SyncField] public int Gold { get; set; } = 0;          // SourceGen → 自动脏
    public GBLList<BagItem> Items { get; set; } = new();    // 容器操作自动脏

    // SourceGen 生成: HasDirty, TakeSyncData(), RestoreFields(), ResetDirty()
    // 默认值 = 有效初始状态, 新玩家首次登录不需要单独初始化
}
```

GBLList 的 DirtySnapshot 记录**操作时的索引**。同一帧内多个操作按顺序 apply，客户端本地列表与服务端操作前一致，结果正确。

### 3.3 Behavior (System)

```csharp
// 单例, 纯逻辑, 构造函数收 DataStore
public class BagBehavior
{
    DataStore _store;
    public BagBehavior(DataStore store) { _store = store; }

    public UseItemResult UseItem(ulong roleId, int itemId, int count)
    {
        var bag = _store.Get<BagBehaviorInfo>(roleId);
        var item = bag.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null || item.Count < count) return Error("道具不足");
        item.Count -= count;
        if (item.Count == 0) bag.Items.Remove(item);  // GBLList → 自动记录操作
        bag.Gold -= price;                              // [SyncField] → setter 自动脏
        return Ok();
    }

    // ★ 跨 Behavior 交互: 只依赖 BehaviorInfo 类型, 不依赖对方 Behavior
    //   Mail 发道具附件 → _store.Get<BagBehaviorInfo>(id).Items.Add(x)
}
```

### 3.4 DataStore

```csharp
public class DataStore
{
    Dictionary<Type, IDictionary> _byType;         // Type → {entityId → data}
    Dictionary<ulong, Dictionary<Type, object>> _byEntity;

    // ★ 懒加载: 内存命中 → 直接返回; 未命中 → 异步 DB 读 → MPSC 回调入索引
    T Get<T>(ulong entityId) where T : class, new();

    // 全量加载 (仅登录时调用)
    void LoadAll(ulong entityId);

    // 遍历 + 脏查询 (SyncEngine)
    DataCollection<T> All<T>();
    IEnumerable<(ulong, T)> GetDirty<T>();

    // 增量回滚
    DirtySnapshot TakeDirty(ulong entityId);
    void Restore(ulong entityId, DirtySnapshot s);

    // 写库 → Truck → WAL → MongoDB
    void Save(ulong entityId);
}
```

`Get<T>` 主线程同步查内存 (O(1))。未命中时发起异步 DB 读，OS 线程完成后通过 MPSC 队列回调主线程补入索引。下帧 `Get<T>` 即可命中。

### 3.5 Behavior 注册

每个 Service 一个 `Register()` 入口。SourceGen 扫描 `[RpcService]` 接口，自动生成 method hash → Behavior 方法映射。

```csharp
public static class PlayerServiceBehaviors
{
    public static void Register(DataStore store, IServiceProvider services)
    {
        var bag    = new BagBehavior(store);
        var mail   = new MailBehavior(store);
        var friend = new FriendBehavior(store);
        var quest  = new QuestBehavior(store);

        RpcDispatcher.Register<IPlayerService>(bag);    // Bag.UseItem → bag.UseItem
        RpcDispatcher.Register<IPlayerService>(mail);   // Mail.Send   → mail.Send
        RpcDispatcher.Register<IPlayerService>(friend); // Friend.Add  → friend.Add
        RpcDispatcher.Register<IPlayerService>(quest);  // Quest.Accept→ quest.Accept
    }
}
```

新增功能 = 创建 `XxxBehavior.cs` + `XxxBehaviorInfo.cs` + `Register()` 加一行，零侵入。

### 3.6 DirtySnapshot — 增量回滚 & 增量推送

```
  DirtySnapshot (MessagePack):
  { entityId: 42,
    infos: [{
      type: "BagBehaviorInfo",
      fields: {mask:0b0001, vals:[90]},     // [SyncField] 脏字段
      containers: [{                        // GBL 容器操作日志
        field:"Items",
        ops: [{op:"update", idx:2, val:{...}}, {op:"add", idx:5, val:{...}}]
      }]
    }]
  }
```

| 场景 | 流程 |
|------|------|
| 增量回滚 | Job 失败 → TakeDirty → Restore (只恢复脏字段, O(1)) |
| 增量推送 | SyncFlush → GetDirty → TakeSyncData → Push → ResetDirty |

脏标记生命周期: `Job.Execute()` 自动脏 → 成功不清理 (留给 SyncFlush) → SyncFlush 推送后 ResetDirty → 失败则 Restore 消费。

### 3.7 各 Service = 不同 Behavior 组合

```
  player.serv:  Bag, Mail, Friend, Quest        club.service: Club
  chat.service: Chat, WorldChannel              rank.service: Rank
  trade.serv:   Trade (2PC)                     auction.svc: Auction
```

---

## 四、何时拆为独立 Service

```
  满足任一 → 独立 Service:
    ① 共享可变状态 (ClubInfo 多人同时写, AuctionListing 多人竞价)
    ② 全局视角 (排行榜, 世界频道, 拍卖行全服可见)
    ③ 中立协调 (2PC 交易)
    ④ 事务模型不同 (聊天/拍卖不需要 Backup/Restore)

  不满足 → 在现有 Service 加 Behavior + BehaviorInfo
```

| 业务 | Service | Behavior | 拆出原因 |
|------|---------|----------|----------|
| 背包 | player.serv | Bag | 私有 |
| 邮件 | player.serv | Mail | 私有 |
| 好友 | player.serv | Friend | 私有, 跨服直连 RPC, 离线写 DB |
| 任务 | player.serv | Quest | 私有 |
| 公会 | club.service | Club | ① 共享可变 |
| 聊天 | chat.service | Chat | ② 全局视角 + ④ 无事务 |
| 排行榜 | rank.service | Rank | ② 全局视角 |
| 拍卖行 | auction.svc | Auction | ① + ② + ④ |
| 交易 | trade.serv | Trade | ③ 中立协调 |

---

## 五、关键流程

### 5.1 登录

```
  Client → Gateway:
    ① 验证账号 (DB) + Rate Limit 检查
    ② 生成 sessionId, 签发 resumeToken (HMAC, 5min 有效期)
    ③ 问 Router: playerId → 哪个 serv? (一致性哈希)
    ④ RPC → player.serv: PlayerJoin(playerId, sessionId, gatewayAddr)
    ⑤ player.serv:
       DataStore.LoadAll(id) — 按需从 DB 加载, 不存在则 new → 默认值
       创建 Role{entityId, session, _jobs}
       向 Router 注册: online:{playerId} = {servAddr, gatewayAddr}
    ⑥ Gateway 缓存: playerId → serv:port (TTL 30s)
    ⑦ 发 resumeToken 给 Client
```

### 5.2 客户端请求与异步响应

```
  Client → Gateway → 查本地缓存 → RPC 直连 player.serv

  PlayerService:
    tcs = new TaskCompletionSource<T>()
    party.EnqueueJob(pid, () => {
        result = behavior.Method(...)
        tcs.SetResult(result)     // Job 执行完 → 设置结果
    })
    return tcs.Task
    ★ 主线程每帧 DrainRpcCallbacks 检查 IsCompleted → 序列化 → 发回 Gateway
```

### 5.3 增量推送 (SyncEngine)

```
  ProcessRoles 之后:

  foreach type in BehaviorInfoTypes:
    foreach (entityId, info) in _store.GetDirty<type>():
      diff = info.TakeSyncData()       // 只取脏字段 + 容器日志
      查 session → RPC 直连 Gateway → Client
      info.ResetDirty()
```

### 5.4 下线与重连

```
  下线:
    Gateway 检测断连 → Router 删除 online:{playerId}
    → player.serv: Role 标记 offline, 保留 roledestroy 秒
    → 缓冲期后: Save → Truck → 销毁

  重连:
    Client 发 resumeToken → Gateway 校验 (5min 内有效)
    → 查 Router: 缓冲期内 → 旧 serv; 已销毁 → 重新登录
    → player.serv: Reconnect → 更新 session → SyncEngine 全量推一次
    → 发新 resumeToken
```

### 5.5 跨服交互 + 离线

```
  Friend.Add(A, B):

  ① 查 Router: "B 在哪?"
  ② 在线 → RPC 直连 B 所在 serv
     离线 → db.Load<FriendBehaviorInfo>(B) (~1KB, ~1ms) → 写入 → WAL → 异步存 DB
  ③ 竞态防护: BehaviorInfo.version 乐观锁 → 冲突自动转在线 RPC
```

**离线交互代价恒定 ~1KB ~1ms，不随玩家总数据增长。**

### 5.6 拍卖行

```
  浏览: Redis 缓存直接返回 (不经过 auction.svc 主线程)

  竞价 (全同步保证一致性):
    auction.svc 主线程:
      ① 验证出价 → 更新内存 listing → 标记脏
      ② 同步写 Redis (浏览者看到的是最新价)
      ③ 异步 RPC → player.serv: 退回上一个竞价者金币 (可接受延迟)
      ④ 返回 BidOk

  成交 (TimerWheel 到期):
    → RPC → player.serv (卖家): Mail + 金币
    → RPC → player.serv (买家): Mail + 物品
    → 归档 MongoDB, 内存清理
```

---

## 六、主循环

### 6.1 Engine (所有进程通用)

```
  while (_running):
      DrainCallbacks()         // MPSC: IO 结果 + TaskCompletionSource 检查
      DrainTimers()            // TimerWheel
      DriveCoroutines()        // IEnumerator 推进
      Publish(Frame)           // 帧事件
      SleepToNextFrame()       // 精确等帧
  FlushAll()
```

### 6.2 各进程 OnFrame

```
  Gateway:  DrainNetwork · DrainRpcCallbacks · DrainTimer
  Router:   DrainRpcCallbacks · DrainTimer (心跳 TTL)
  player:   DrainRpcCallbacks · DriveCoroutines · ProcessRoles · SyncFlush · TruckCheck
  club:     DrainRpcCallbacks · ProcessOps · TruckCheck
  chat:     DrainRpcCallbacks · PushMessages · TruckCheck
  auction:  DrainRpcCallbacks · DriveCoroutines · ProcessBids · TruckCheck
  trade:    DrainRpcCallbacks · DrainTimer (超时) · DriveCoroutines
```

### 6.3 ProcessRoles (公平调度 + 增量回滚)

```
  foreach role in _roles.OrderByDescending(r => r.StarvationFrames):
      budget = Clamp(baseBudget + starvationFrames/3, min:5, max:25)
      while processed < budget && role.HasJobs:
          job = role.DequeueJob()
          try:
              job.Execute()         // void, 主线程, 自动脏
              job.Complete()        // TaskCompletionSource.SetResult
              role.FlushSends()
          catch:
              snapshot = _store.TakeDirty(role.entityId)
              _store.Restore(role.entityId, snapshot)   // 增量回滚
              job.Fail(ex)
              role.ClearSends()
          processed++
      role.StarvationFrames = role.HasJobs ? role.StarvationFrames + 1 : 0
      if role.StarvationFrames > 60 → Alert + 考虑热迁移
```

---

## 七、安全

### Gateway 入口防护

```
  Rate Limit:    每 IP 每秒 N 次 (Token Bucket, Redis 计数器)
  Token 校验:    每个 RPC 携带 session token, Gateway 校验 HMAC
  DDoS 基础:     MaxConnections / MaxPacketSize / ConnectionTimeout
  Service 间通信: 内网 RPC + HMAC 签名
```

---

## 八、扩容

### 8.1 单实例容量

```
  player.serv: 5000 Role (保守) → 30MB 内存 → 0.1ms/帧
               可到 20000 Role → 120MB 内存 → 0.7ms/帧
               ★ 真正上限在 GC 零分配和公平调度粒度, 不在 CPU

  Gateway:     10000 连接 → 5MB 内存 → 2ms/帧
```

### 8.2 100 万在线配置

```
  ┌────────────────┬──────────┬──────────┐
  │ Service        │ 实例数   │ 机器数   │
  ├────────────────┼──────────┼──────────┤
  │ player.serv    │ 200      │ ~13      │
  │ Gateway        │ 100      │ ~4       │
  │ Router         │ 2        │ 复用     │
  │ club/chat/等   │ 各 1~N   │ 复用     │
  ├────────────────┼──────────┼──────────┤
  │ 总计           │ ~310     │ ~17-20   │
  └────────────────┴──────────┴──────────┘

  压力: player.serv CPU ~5% · Gateway CPU ~10% · Router 可忽略
  MongoDB 分片 10 节点 · 内网 10Gbps 轻松
```

### 8.3 扩缩容对象

```
  ★ 自动扩缩容: player.serv (有状态, 需热迁移), Gateway (无状态, 秒级)
  ★ 手动扩缩容: club/chat/rank/auction/trade (有状态但单实例够用; 分片后可选自动)
```

### 8.4 Role 热迁移

```
  单 Role: Freeze (停 Job, 等协程 ≤5s) → 序列化 (~6KB) → RPC → 反序列化 → Resume
  耗时 ~200ms | 并行 50 个/批 × 200ms = 200ms/批
  缩容 5000 Role: 100 批 ≈ 20s
  玩家体感: < 1s 暂停

  迁移一致性:
    新 serv 向 Router 注册 → 覆盖 online:{playerId}
    Gateway 缓存未更新 → 旧 serv 返回 Redirect → 自动重试

  目标实例 Crash (迁移中):
    已迁移 Role 从源实例快照回退 (MongoDB + WAL), 未迁移继续服务
    Controller 取消本次迁移, 选新目标重试
```

### 8.5 扩缩容 = 部署配置

```
  扩容: 启动新实例 → Router 更新 hashring → 热迁移均衡负载 → 完成
  缩容: 标记 draining → 迁移全部 Role → 进程退出 → Router 移除
  ★ Behavior 代码零改动
```

---

## 九、自动扩缩容 (Controller)

### 9.1 架构

Controller 是运维中控进程。Monitor → Decider → Executor → CloudDriver，完整闭环。

### 9.2 指标采集

```csharp
[RpcService]
public interface IStatsService
{
    [RpcMethod] Task<ServiceStats> GetStats();
}

public class ServiceStats
{
    string ServiceType, InstanceId;
    int EntityCount, ActiveEntities, TotalQueueDepth, DbConnectionCount;
    float CpuPercent, AvgJobLatencyMs;
    long MemoryMB;
    uint FrameNumber;
}
```

Controller 每 5s 拉取全集群 `/stats`。新进程 Ready 检测: 轮询 Router 注册列表 → 连续 3 次心跳 OK → 标记 running → 触发迁移。

### 9.3 决策规则

```
  扩容 (满足任一, 冷却 3min):
    ① 集群 CPU 均值 > 70%, 持续 30s
    ② 任一实例 EntityCount > 20000
    ③ 任一实例 AvgJobLatencyMs > 10ms, 持续 30s

  缩容 (全部满足, 冷却 3min):
    ① 集群 CPU 均值 < 30%, 持续 5min
    ② 所有实例 EntityCount < 上限 50%
    ③ 实例数 > minReplicas (默认 3)

  每次只扩/缩 1 个实例, 30s 观察期后再评估, 防止震荡
```

### 9.4 Cloud Driver (可插拔)

```csharp
interface ICloudDriver
{
    Task<Machine> Lease(string spec, string startupScript);
    Task Release(string machineId);
    Task<Machine[]> ListMachines();
}
```

换云平台 = 换 Driver 实现。推荐按需实例，Spot 实例仅适用于 Gateway/Router 无状态服务。

### 9.5 Controller HA

```
  Controller#1 (主) ← Redis 锁 "controller:leader" (TTL 10s) → Controller#2 (备)
  锁丢失 → 降级 | 锁过期 → 抢占
  同一时刻仅一个决策, 主备切换 10s 无影响
```

### 9.6 DB 连接池

```
  200 player.serv 实例 × 10 MongoDB 连接 = 2000 连接 (安全, 上限 65536)
  如超: MongoDB Proxy (mongos) 连接复用; 或增加分片
```

### 9.7 成本

```
  100 万在线 (AWS, 按需, 锯齿状流量):

  日均 120 实例 (峰值 200, 低谷 50) → 15 台 c5.4xlarge
  15 × 24h × 30d × $0.68 = ~$7344/月

  无自动缩容 (始终 25 台): ~$12240/月
  ★ 自动缩容节省 ~$4900/月 (40%)
```

### 9.8 实施优先级

```
  Phase 1: 指标采集 + 告警, 人工扩缩容
  Phase 2: 自动决策 + 人工确认
  Phase 3: 全自动 + Cloud Driver, 零人工介入
```

---

## 十、单实例故障恢复

```
  club.service Crash:
    Controller 检测心跳丢失 → Cloud Driver 启动新实例
    → MongoDB 加载全部 BehaviorInfo → WAL 回放 → Router 注册 → 流量恢复
    5000 实体 ~5 秒
    恢复期间: Gateway 返回 "服务暂不可用"
```

---

## 十一、零 GC 策略

```
  MessagePack buffer:   ArrayPool<byte>.Shared 复用
  临时集合:             GBLList/GBLDict 从 ObjectPool 租用, 帧末归还
  字符串:               Span<char> + Utf8Formatter 替代 string.Format
  TaskCompletionSource: 对象池复用 (完成时归还)
  ★ 核心路径每帧 GC.Alloc = 0
```

---

## 十二、配置管理

```json
// appsettings.json → appsettings.Production.json → 环境变量覆盖
{
  "Queen": {
    "Gateway": { "Port": 12801, "MaxConnections": 10000, "RateLimit": {"PerSecond": 50} },
    "Router": { "Port": 10010, "Redis": "redis-sentinel:26379,service=queen-redis" },
    "PlayerService": { "Port": 10020, "MaxRoles": 20000, "RoleDestroySeconds": 300, "JobBudgetPerFrame": 5 },
    "Persistence": { "MongoDB": "mongodb://mongo:27017/queen", "WalPath": "/data/wal/" }
  }
}
```

`IOptions<T>` 绑定，环境分层，无需分散的 settings 文件。

---

## 十三、测试策略

### 13.1 单元测试

```csharp
[Test]
void UseItem_RemovesItem_WhenCountReachesZero()
{
    var store = new DataStore(new MockDatabase());
    var bag = new BagBehavior(store);
    store.Load<BagBehaviorInfo>(roleId).Items.Add(new BagItem { Id = 42, Count = 1 });
    var result = bag.UseItem(roleId, 42, 1);
    Assert.Ok(result);
    Assert.Empty(store.Get<BagBehaviorInfo>(roleId).Items);
}
```

Behavior + DataStore 两个类独立可测，零 Mock 开销。

### 13.2 集成测试

```
  启动测试集群 → 登录 → 客户端请求 → SyncPush → 跨服好友(在线+离线)
  → 下线 → resumeToken 重连 → Role 热迁移 → 故障恢复 (kill → 重启 → 数据完整)
```

### 13.3 覆盖率

```
  Queen.Core: 90%+ | Queen.Rpc/Server: 80%+ | Queen.Network/Persistence: 60%+
```

---

## 十四、项目结构

```
Queen.sln
├── src/
│   ├── Queen.Core/            # Engine, Comp, EventBus, Ticker, TimerWheel
│   │   ├── Async/             # MpscQueue, CallbackSink
│   │   ├── Containers/        # GBLList, GBLDict (自带脏标记)
│   │   └── Scheduling/        # CoroutineScheduler
│   ├── Queen.Rpc/             # [RpcService] [RpcMethod] [SyncField], DirtySnapshot, SourceGen
│   ├── Queen.Network/         # ITransport, TCP/WS/UDP
│   ├── Queen.Persistence/     # MongoRepository, BatchWriter, WAL
│   ├── Queen.Gateway/         # SessionManager, AuthPipeline, RateLimiter
│   ├── Queen.Router/          # ServiceRegistry(Redis), LookupService
│   ├── Queen.Controller/      # Monitor, Decider, Executor, CloudDriver
│   ├── Queen.Server/          # player.serv: DataStore, Behaviors, Party, SyncEngine, Truck
│   ├── Queen.Club/            # club.service
│   ├── Queen.Chat/            # chat.service + WorldChannel
│   ├── Queen.Rank/            # rank.service
│   ├── Queen.Auction/         # auction.svc
│   ├── Queen.Trade/           # trade.serv (2PC)
│   ├── Queen.Ration/          # HTTP 管理 API
│   ├── Queen.Bot/             # 压测
│   └── Queen.DBObserve/       # DB 观测
├── tests/  (Core / Rpc / Server / Integration)
├── configs/
└── analyzers/Queen.Analyzers/
```

---

## 十五、设计决策

### 架构决策

| # | 决策 | 理由 |
|---|------|------|
| 1 | **Router DNS 模式** | 只寻址不转发; 全直连; 压力极低 |
| 2 | **Redis Sentinel HA** | 主从 + 自动故障转移; 不可用时本地缓存降级 |
| 3 | **Behavior/BehaviorInfo 分离** | 单例 System + 数据 Component; 统一 Service 骨架 |
| 4 | **DataStore 懒加载** | Get<T> 触发; 离线单类型 ~1KB ~1ms |
| 5 | **脏标记双重用途** | 增量回滚 + 增量推送; 零额外类 |
| 6 | **离线写走 WAL + 乐观锁** | 同在线路径; version 冲突自动转在线 |
| 7 | **下线缓冲期** | roledestroy 秒内可重连; resumeToken HMAC 跨 Gateway |
| 8 | **Gateway 安全入口** | Rate Limit + Token + 连接限制 |
| 9 | **读写分离** | rank/auction 查询走 Redis 缓存 |
| 10 | **扩缩容 = 部署配置** | 加实例只改 Router hashring; Behavior 代码零改动 |
| 11 | **Controller 自动化** | Monitor → Decider → Executor → CloudDriver 闭环 |
| 12 | **单实例故障 WAL 恢复** | MongoDB + WAL 回放; 分钟级 |
| 13 | **Service 间直连** | Router 不碰业务流量 |

### 实现决策

| # | 决策 | 理由 |
|---|------|------|
| 14 | **多进程单线程** | 进程内无锁; Task 仅限 IO 层 |
| 15 | **IEnumerator 协程** | 跨帧同步写法; 主线程驱动; 不阻塞 |
| 16 | **公平调度** | 饥饿感知 + 动态预算; 帧时间可控 |
| 17 | **干掉 Protocols** | [RpcService] + SourceGen; MessagePack 统一序列化 |
| 18 | **零分配** | ArrayPool + Span<T> + 对象池; GC.Alloc=0 |
| 19 | **EventBus 快照派发** | ImmutableList |
| 20 | **TimerWheel O(1)** | 替代线性列表 |
| 21 | **WAL + 失败回调** | 崩溃恢复; 3 次失败告警 |
| 22 | **IOptions 统一配置** | 环境分层 |
| 23 | **Behavior 独立可测** | DataStore + Behavior 两个类; 零 Mock |

---

## 十六、实施阶段

| 阶段 | 内容 |
|------|------|
| Phase 1 | Queen.Core (Engine, Comp, EventBus, Ticker, TimerWheel, GBLList/GBLDict, MpscQueue, CoroutineScheduler, 零分配验证) |
| Phase 2 | Queen.Rpc + SourceGen ([RpcService], [SyncField]) |
| Phase 3 | Queen.Network (ITransport, TCP/WS/UDP) |
| Phase 4 | Queen.Persistence (MongoRepository, BatchWriter, WAL) |
| Phase 5 | Queen.Router (ServiceRegistry+Redis Sentinel, LookupService) |
| Phase 6 | Queen.Gateway (SessionManager, resumeToken, AuthPipeline, RateLimiter) |
| Phase 7 | Queen.Server (DataStore+懒加载, Behaviors, Party+公平调度+回滚, SyncEngine+推送) |
| Phase 8 | Queen.Club, Queen.Chat, Queen.Rank, Queen.Auction |
| Phase 9 | Queen.Trade (2PC) |
| Phase 10 | Queen.Controller (Monitor, Decider, Executor, CloudDriver) |
| Phase 11 | Queen.Ration, Queen.Bot, Queen.DBObserve, Analyzers |
| Phase 12 | Tests & Polish (80%+ 单元覆盖, 集成测全部关键流程) |
