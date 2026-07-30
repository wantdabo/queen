# Queen 游戏服务端架构方案

> **版本**: v0.6 · **日期**: 2026-07-30
> **状态**: 本文档是**目标态设计**与**唯一真理**。当前仓库代码为旧实现,已废弃,将按本文档从头实现。文中标注 `(目标)` 的特性尚未落地。

## 实现状态声明

| 模块 | 状态 |
|------|------|
| 旧代码（Queen/Queen.Server 等） | 废弃,不作为参考 |
| 本文档 | 唯一设计真理,实现细节可调整,设计哲学与痛点不可破坏 |
| 多进程拓扑（Gateway/Router/Controller/各 Service） | `(目标)` 未实现 |
| 核心特性（[Persistent]/[Projector] 投影、ProjectorSystem、TGBLList、DataStore 懒加载、WAL、TimerWheel、热迁移） | `(目标)` 未实现 |

**读本文档前请知**:这是"要做成什么",不是"现在是什么"。

---

## 核心哲学

**多进程,进程内单线程。IO 异步,业务同步。数据逻辑分离,容器自带脏标记。Actor 永久可寻址,激活对调用方透明。**

### 核心原则

| 原则 | 说明 |
|------|------|
| **进程内单线程** | 整个进程业务层**只有一个线程**;所有 Actor 在该线程上通过协程交替执行;**绝对无锁**,所有数据结构裸用;多核靠多进程(多实例) |
| IO 异步 offload | 网络收发、DB 读写走 OS 线程池,结果通过 MPSC 队列回进程唯一业务线程 |
| 业务逻辑同步 | 所有业务方法为 `void` 或返回纯值,单线程执行,**禁止 `async`**(async 引入线程池调度,破坏单线程确定性) |
| **协程即调度** | Actor 的 Job 在单线程上**协程交替**推进;跨帧等待(定时、DB 读未命中)或跨进程等待(RPC 响应)时 `yield`,调度器切到下一个 Actor,结果回来再 resume |
| **Virtual Actor** | Actor 永久可寻址(借鉴 Orleans)。在线/离线是业务状态(有无 session/gateway),不是代码路径。对调用方透明:Actor 在内存则直接调,不在则创建壳+按需懒加载 Get<T>。无 Version/乐观锁/离线白名单 |
| **Behavior/BehaviorInfo 分离** | Behavior = System (单例逻辑,建议无状态以利热迁移); BehaviorInfo = Component (纯数据) |
| **[Persistent]/[Projector] 双标志** | 一份 BehaviorInfo 结构,字段用 class 级 `[Persistent]`(写盘)/`[Projector]`(推送)独立标记。对齐 KBEngine PERSISTENT/CLIENT、UE SaveGame/Replicated。不分两份数据 |
| **脏只推送,不回滚** | dirty(projectdirtymask + TGBLList CollectDiff)只用于增量推送。回滚干掉,数据安全靠四件套(见 5.9) |
| **派生事件驱动** | 派生字段(如 total=gold+money)在 OnEnter(全量)/RPC(增量)/OnLeave(清理)算,不 OnTick 全员扫 |
| **零分配** | 核心路径低 GC; 对象池 + `Span<T>` + `ArrayPool`; 目标 `< 1KB/帧` |

### 非目标(明确不做)

| 不做 | 理由 |
|------|------|
| 客户端预测/状态插值 | Queen 是服务端框架,预测属客户端(goblin)职责 |
| 实时语音/视频 | 非游戏业务逻辑,走专用媒体服务 |
| 跨服物理战斗 lockstep | 本框架面向异步业务(背包/邮件/公会/交易),lockstep 需独立帧同步服务 |
| 通用 ORM/SQL 抽象 | 游戏数据访问模式固定(MessagePack + MongoDB),ORM 是负担 |
| 分布式事务 2PC 用于玩家交易 | 用"冻结-确认"模式替代,见 5.7 |
| **单进程多线程** | 多核靠多进程;单进程多线程引入锁/竞态,破坏单线程确定性,与 goblin 不同构 |
| **内存业务层事务回滚** | 行业普遍不做(Skynet/KBEngine 等);失败 99% 在校验阶段;靠四件套替代,见 5.9 |
| **深嵌套容器** | List 套 List 套 Dict 拍平成复合 key Dict 或拆 BehaviorInfo(对齐 goblin/KBEngine) |

### IO / 业务边界(硬约束)

```
                    ┌──────────────────────────────────────┐
                    │         IO 层 (允许 async/Task)       │
                    │  Queen.Network / Queen.Persistence   │
                    │  OS 线程池: 收发 / DB 读写 / WAL 落盘  │
                    └──────────────┬───────────────────────┘
                                   │ MPSC Queue (唯一跨线程接触点)
                    ┌──────────────▼───────────────────────┐
                    │  业务层 (进程内单线程,禁止 async)      │
                    │  所有 Actor 在唯一业务线程上           │
                    │  通过协程交替推进                      │
                    │  Behavior 方法 → void                │
                    │  IEnumerator (跨帧/跨进程 yield)      │
                    └──────────────────────────────────────┘
```

CI 中 Roslyn Analyzer 拦截:
- `QN1001`: 业务层任何 `async` 方法(async 会把后续逻辑扔到线程池,破坏单线程确定性)

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
   │              QUEEN.ROUTOR (2~N 实例, HA)       │
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

**每个进程内部单线程,多核靠多实例。Router 管寻址,Controller 管扩缩容,职责分离。所有 Service 之间直连通信。**

---

## 二、Router — DNS 模式

### 2.1 数据模型

```
  Redis (Sentinel/Cluster, HA):
    services:{type}          → Set<instanceAddr>
    services:{type}:hashring → SortedSet<slot, instanceAddr>
    online:{playerId}        → {servAddr, gatewayAddr}  (TTL 5s, player.serv 心跳续期)
    players:{playerId}       → 1  (永久, 首次登录写入, 表示该玩家已注册)

  Router 本地缓存: 500ms 刷新
  Gateway/Service 本地缓存: 30s TTL
```

### 2.2 寻址

```
  Router.Lookup(actorId, serviceType):

    if serviceType == "player":
      ① 查 online:{actorId} → 命中 → {serv, gateway, status:"online"}
      ② 未命中 → 查 players:{actorId} (已注册玩家集合)
         - 存在 → hashring 算 home serv → {serv, gateway:null, status:"offline"}
         - 不存在 → {status:"not_found"} (新玩家/从未注册)
    else:
      查 hashring → 实例地址 (club: clubId hash, chat: roomId hash, ...)

  ★ 始终返回 serv 地址 (除非玩家不存在)
  ★ 调用方不关心在线/离线 — 有 serv 地址就发 RPC, 目标 serv 负责激活 Actor
```

### 2.3 缓存失效与迁移重定向

```
  Actor 迁移后 Gateway 可能缓存指向旧 serv:

  Gateway → 旧 serv → 返回 Redirect {newServ}
  Gateway 更新缓存 → 重试到新 serv

  ★ 重试上限 3 次, 超出返回失败 (防 Redirect 循环)
  ★ 每个 Redirect 携带 hop 计数, Service 检测 hop > 3 直接拒绝
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

**所有业务进程共用一个骨架。player.serv 不特殊。每个进程内部单线程。**

### 3.1 术语

| 概念 | 是什么 | 数量 |
|------|--------|------|
| **BehaviorInfo** | Component,纯数据; class 级 `[Persistent]`(写盘)/`[Projector]`(推送)特性声明字段,类体空 | 每实体每类型一份 |
| **Behavior** | System,单例逻辑,对应一种 BehaviorInfo,建议无状态(利热迁移) | 每种 BehaviorInfo 一个 |
| **DataStore** | 存储,`Get<T>` 懒加载;单线程访问,无需锁,裸 Dictionary | 每个 Service 一个 |
| **[Persistent]** | class 级特性,声明写盘字段;SG 生成 MessagePack 持久化序列化 | — |
| **[Projector]** | class 级特性,声明推送字段;SG 生成 backing field + 属性 + `projectdirtymask` 脏位 + `IProjectable`。对齐 goblin | — |
| **projectdirtymask** | ulong 位图,每个 [Projector] 字段一位,set 时置位 | 每个 BehaviorInfo 一个 |
| **GBLList / GBLDict** | 集合容器(写盘用);**TGBLList / TGBLDict** 带脏追踪(推送用),`CollectDiff` 出 added/removed 索引差量 | — |
| **ProjectorSystem** | 帧末收集脏 BehaviorInfo → ProjectorPacket;1:1 复用 goblin | 每个 Service 一个 |
| **Actor** | 数据归属单元, 走 Stage 调度/协程交替; 不同进程装不同 Behavior | player.serv=玩家, club=公会, chat=房间, auction=拍卖listing |
| **Stage** | 调度器,单线程协程交替调度 Actor 的 Job,公平调度 | 每个 Service 一个 |

### 3.2 BehaviorInfo (Component) — class 级特性声明,类体空

**对齐 goblin FacadeInfo/SpatialInfo**:字段声明在 class 级 `[Persistent]`/`[Projector]` 特性里,类体空,SG 生成一切。

```csharp
// 写盘 + 推送
[Persistent("gold",  typeof(int))]  [Projector("gold",  typeof(int))]
[Persistent("money", typeof(int))]  [Projector("money", typeof(int))]
// 派生: 只推不写库 (OnEnter/RPC 算, 见 3.6)
[Projector("total", typeof(int))]
// 写盘 + 推送
[Persistent("name", typeof(string))] [Projector("name", typeof(string))]
// 只写 (敏感不推)
[Persistent("age", typeof(int))]
// 容器: 一层扁平, 元素 struct/不可变 class (写盘用 GBLList, 推送用 TGBLList)
[Persistent("items", typeof(GBLList<Item>))] [Projector("items", typeof(TGBLList<Item>))]
public partial class PlayerBehaviorInfo : BehaviorInfo { }   // 类体空
```

**字段组合**(对齐 KBEngine PERSISTENT/CLIENT、UE SaveGame/Replicated):
- `[Persistent]+[Projector]`:写盘 + 推送(gold/name/items)
- `[Persistent]` only:只写不推(age,敏感)
- `[Projector]` only:只推不写(total,派生;hp,运行时)
- 都不标:内部状态,不写不推

### 3.3 Behavior (System)

```csharp
public class WalletBehavior : Behavior<PlayerBehaviorInfo>
{
    readonly DataStore _store;            // readonly 引用, 允许
    readonly WalletConfig _config;

    public WalletBehavior(DataStore store, WalletConfig config)
    { _store = store; _config = config; }

    // ★ 建议无状态 (利于热迁移协程重建); 单线程下非硬约束

    [RpcMethod]
    public void Spend(ulong actorId, int cost)
    {
        var p = _store.Get<PlayerBehaviorInfo>(actorId);
        if (p.gold < cost) return;            // 先校验
        p.gold -= cost;                        // 改源 → SG 生成的 setter 置 projectdirtymask
        p.total = p.gold + p.money;            // 增量算派生 → 置 total 脏位
    }

    // ★ 跨 Behavior 交互: 只依赖 BehaviorInfo 类型, 不依赖对方 Behavior
    //   Mail 发道具附件 → _store.Get<BagBehaviorInfo>(id).items.Add(x)
}
```

### 3.4 SourceGen 生成(对齐 goblin FacadeInfo.projector.g.cs)

```csharp
partial class PlayerBehaviorInfo : IProjectable
{
    private int player_gold, player_money, player_total;
    private string player_name;  private int player_age;
    private TGBLList<Item> player_items = new();

    public ulong projectdirtymask { get; set; }   // 推送脏位图

    public int gold {
        get => player_gold;
        set { if (player_gold != value) { player_gold = value; projectdirtymask |= 1ul << GOLD_BIT; } }
    }
    // money/total/name/items 同理 (set 置脏位)
    // age 无 [Projector] → 不生成脏位 (纯写盘, SG 只生成持久化序列化)

    public object[] TakeProjectValues(ulong mask) { ... }   // 按掩码取推送值
    public void ClearProjectDirty() => projectdirtymask = 0;
    public void MarkAllDirty() => projectdirtymask = ALL_BITS;  // 新对象全量推

    // [Persistent] 字段 → SG 额外生成 MessagePack 持久化序列化 (WriteTo/ReadFrom)
    // total/items(推送)/无[Persistent]的字段不进持久化序列化
}
```

**`[Persistent]` + `[Projector]` 同字段**:SG 独立生成两套(脏位推送 + MessagePack 序列化),互不干扰。

### 3.5 DataStore (单线程无锁 + 懒加载)

```csharp
public class DataStore
{
    // ★ 单线程访问, 裸 Dictionary, 无锁
    Dictionary<Type, IDictionary> _byType;         // Type → {actorId → data}
    Dictionary<ulong, Dictionary<Type, object>> _byActor;

    // ★ 懒加载: 内存命中 → 返回; 未命中 → 挂起 Job, 异步 DB 读, MPSC 回调入索引, 下帧重试
    T Get<T>(ulong actorId) where T : class, new();

    void LoadAll(ulong actorId);                    // 全量加载 (登录时)

    IEnumerable<IProjectable> GetProjectables();    // ProjectorSystem 遍历用
    void Save(ulong actorId);                       // 写库 → Truck → WAL → MongoDB
}
```

**`Get<T>` 懒加载时序**:

```
  Job 执行中调用 Get<T>(id):
    ① 命中内存 → 返回 (O(1), 无锁)
    ② 未命中:
       - 标记当前 Job 状态 = Yield
       - 发起异步 DB 读 (IO 层, OS 线程)
       - Job 挂起 → 调度器切到下一个 Actor (单线程不阻塞)
       - DB 读完成 → MPSC 队列 → 回业务线程 → 写入索引
       - 下一帧该 Job resume → Get<T> 命中 → 继续

  协程场景: yield return null 重试 (见 5.2)
```

**注**: `Get<T>` 返回 `null` 仅表示"本次未命中,已发起加载",业务方用协程 `yield return null` 重试。绝不返回 null 让业务跑下去。单线程下 null 判断绝对可靠。

### 3.6 派生字段计算时机(事件驱动,不 OnTick)

派生字段(如 total = gold + money)在三个事件点算,**不每帧全员扫**:

| 时机 | 触发 | 做什么 |
|------|------|--------|
| **OnEnter** | LoadAll 后 Actor 激活 | 全量算派生 (`total = gold + money`) |
| **RPC** | 业务请求改源 | 增量算派生 (改 gold 的同处算 total) |
| **OnLeave** | ActorDestroySeconds 缓冲期结束销毁前 | 清理临时派生 |

```csharp
public class WalletBehavior : Behavior<PlayerBehaviorInfo>
{
    public override void OnEnter(PlayerBehaviorInfo p) => p.total = p.gold + p.money;  // 全量
    [RpcMethod] public void Spend(ulong id, int cost) { var p=store.Get<>(id); if(p.gold<cost)return; p.gold-=cost; p.total=p.gold+p.money; }  // 增量
    public override void OnLeave(PlayerBehaviorInfo p) { }  // 清理
}
```

- 不 OnTick 全员扫派生(单线程扛不住)
- 不 DependsOn 字段联动(复杂业务炸)
- 不 Rules 推送时算派生(派生在事件点算)
- 定时推进(buff/移动)用 TimerWheel/协程,只给需要的

### 3.7 容器(一层扁平 + 替换式 + CollectDiff)

**一层容器,扁平元素**(对齐 goblin/KBEngine):
- `TGBLList<Item>` / `TGBLDict<uint,Item>`,元素 struct 或不可变 class
- 深嵌套(List 套 List)拍平成复合 key `Dict<(page,slot),Item>` 或拆 BehaviorInfo

**替换式**(goblin 契约):
- 元素改 = 替换整个元素 `items[2] = item with { value = 10 }`
- 替换触发 TGBLList `set` 标 addedindices → 容器脏
- 原地改元素内部(`items[2].value=10`)不追踪,必须替换

**CollectDiff**(1:1 复用 goblin TGBLList):
```
CollectDiff() → { addedindices, removedindices }   // 元素级差量, 无 oldVal
ProjectorSystem 推: 只推新增/删除的索引, 不全量重发
```
比 KBEngine(容器整体重发)精细。

### 3.8 Behavior 注册

```csharp
public static class PlayerServiceBehaviors
{
    public static void Register(DataStore store, IServiceProvider services)
    {
        var wallet = new WalletBehavior(store, services.GetRequired<WalletConfig>());
        var bag    = new BagBehavior(store, services.GetRequired<BagConfig>());
        RpcDispatcher.Register<IPlayerService>(wallet);
        RpcDispatcher.Register<IPlayerService>(bag);
    }
}
```
新增功能 = 创建 `XxxBehavior.cs` + `XxxBehaviorInfo.cs` + `Register()` 加一行,零侵入。

### 3.9 各 Service = 不同 Behavior 组合

```
  player.serv:  Bag, Mail, Friend, Quest, Wallet   club.service: Club
  chat.service: Chat, WorldChannel                 rank.service: Rank
  trade.serv:   Trade (冻结-确认, 见 5.7)           auction.svc: Auction
```

---

## 四、何时拆为独立 Service

```
  满足任一 → 独立 Service:
    ① 共享可变状态 (ClubInfo 多人同时写, AuctionListing 多人竞价)
    ② 全局视角 (排行榜, 世界频道, 拍卖行全服可见)
    ③ 中立协调 (冻结-确认交易)
    ④ 事务模型不同 (聊天/拍卖不需要持久化事务)

  不满足 → 在现有 Service 加 Behavior + BehaviorInfo
```

| 业务 | Service | Behavior | 拆出原因 |
|------|---------|----------|----------|
| 背包 | player.serv | Bag | 私有 |
| 邮件 | player.serv | Mail | 私有 |
| 好友 | player.serv | Friend | 私有, 跨服直连 RPC, 离线 Actor 透明激活 |
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
    ⑤ player.serv (业务线程):
       DataStore.LoadAll(id) — 从 DB 加载 [Persistent] 字段; 不存在则 new → 默认值
       创建 Actor{actorId, session, _jobs} → 加入 Stage
       ★ OnEnter: 全量算派生 (total = gold+money 等 [Projector] only 字段)
       向 Router 注册: online:{playerId} = {servAddr, gatewayAddr}
       ★ 首次登录: SET players:{playerId} = 1 (标记已注册, 供 Router 判断"不存在 vs 离线")
    ⑥ Gateway 缓存: playerId → serv:port (TTL 30s)
    ⑦ 发 resumeToken 给 Client
```

### 5.2 客户端请求与协程化响应

```
  Client → Gateway → 查本地缓存 → RPC 直连 player.serv

  player.serv 业务线程收到 RPC → 封装为 Job 入 Stage:
    IEnumerator HandleSpend(actorId, cost) {
        var p = _store.Get<PlayerBehaviorInfo>(actorId);
        if (p == null) { _store.RequestLoad<>(actorId); yield return null; p = _store.Get<>(actorId); }
        _walletBehavior.Spend(actorId, cost);   // 同步: 改源+算派生+置脏位
        Reply(Ok());                             // 直接序列化响应, 入 Gateway 发送队列
        // ProjectorSystem 帧末统一推送脏字段
    }

  ★ 无 TaskCompletionSource, 无轮询, 无额外分配
  ★ 响应延迟 = Job 排队延迟 + 1 帧 (典型 < 5ms)
  ★ yield 期间该 Actor 让出线程, 别的 Actor 推进, 单线程不阻塞
  ★ 大多数业务是纯同步 Job (无 yield), 一步完成; 跨帧/跨进程/DB未命中才 yield
```

### 5.3 增量推送 (ProjectorSystem, 1:1 复用 goblin)

```
  ProcessActors 之后 (帧末):

  foreach proj in stage.cache.projectables:        // 每个 BehaviorInfo 独立注册
    if (proj.projectdirtymask == 0) continue;       // 99% 跳过, μs 级
    var info = proj as BehaviorInfo;
    if (null == info || !info.active || info.actor==0) continue;

    var packet = ObjectCache.Ensure<ProjectorPacket>();   // 池化
    packet.actor = info.actor;
    packet.behaviorinfotype = info.GetType();
    packet.fieldmask = proj.projectdirtymask;
    packet.values = proj.TakeProjectValues(mask);          // 字段脏值
    CollectContainerDiffs(info, mask, packet);             // TGBLList.CollectDiff 容器差量
    proj.ClearProjectDirty();                              // 清脏
    packets.Add(packet);

  → ProjectionPipeline (Rules 裁剪/隐藏/格式化, 无状态) → Transport → 客户端
```

- 字段级:`projectdirtymask` 位图,只推脏字段
- 容器级:`TGBLList.CollectDiff` 元素级差量(added/removed 索引),不全量重发
- Rules 只裁剪(隐藏/格式化),**不算派生**(派生在 OnEnter/RPC 算)

### 5.4 下线与重连

```
  下线:
    Gateway 检测断连 → Router 删除 online:{playerId}
    → player.serv: Actor 标记 offline (session=null, 不可推送)
    → 保留 ActorDestroySeconds 秒 (缓冲期)
    → 缓冲期后: OnLeave → Save([Persistent]字段) → Truck → 销毁

  重连:
    Client 发 resumeToken → Gateway 校验 (5min 内有效)
    → 查 Router: 缓冲期内 → 旧 serv; 已销毁 → 重新走登录流程
    → player.serv: Reconnect → 绑定 session → ProjectorSystem MarkAllDirty 全量推一次
    → 发新 resumeToken

  ★ 重连保活关键: 缓冲期内 Actor 不销毁, 内存数据完整, 无需重新 LoadAll
  ★ resumeToken 跨 Gateway 有效 (HMAC 签发, Gateway 共享密钥)
  ★ 缓冲期内的 Actor 可被其他玩家正常 RPC 交互 (在线/离线仅影响推送)
```

### 5.5 跨服交互 + 离线 — Virtual Actor 模型

**核心理念**: 在线/离线是业务状态(session 有无),不是代码路径。Behavior 不关心目标是否在线 — Actor 在内存则直接调,不在则激活后调。借鉴 Orleans 的 grain 永远可寻址思想。

```
  Friend.Add(A, B):

  ① 查 Router: "B 在哪?"
     → {serv: player.serv#X, gateway: gw#Y, status:"online" }  // B 在线
     → {serv: player.serv#X, gateway: null,   status:"offline"}  // B 离线,仍在 home serv
     → {status:"not_found"}                                       // B 从未注册,拒绝

  ② RPC → player.serv#X: Friend.Add(A, B)
     同一代码路径:
       player.serv#X 业务线程:
         ③ Actor 在内存? → 直接执行
            Actor 不在内存? → 创建 Actor 壳 (session=null, 空 BehaviorInfo 索引) → 加入 Stage → 执行
         ④ _store.Get<FriendBehaviorInfo>(B) 触发懒加载:
            → 内存未命中 → 发起 DB 读 (仅 FriendBehaviorInfo, ~200B, ~0.5-1ms)
            → Job yield → 切到其他 Actor
            → 下帧 DB 回调 → resume → friends.Add(A)
            // 脏位正常置位 → Truck → WAL → MongoDB (与在线完全相同的持久化路径)
         ⑤ 回复 Ok()
         ⑥ Actor 保留 ActorDestroySeconds 后钝化
         ⑦ 如果有 session → ProjectorSystem 推送脏字段; 无 session → 仅写库

  全程: 无 CAS / 无 Version / 无两套代码路径
  ★ 离线激活不做 LoadAll — 不预加载全量数据
```

**离线交互代价 (IO 视角)**:
- Actor 不在内存: 首次按需懒加载单个 BehaviorInfo (~200B-5KB, ~0.5-2ms) + 正常业务执行
- Actor 在内存 (缓冲期内): 零 IO, 纯内存访问
- 业务逻辑触及其他 BehaviorInfo → 按需懒加载, 每步独立 yield
- ★ 不复用在线登录的 LoadAll (全量 50-200KB, 5-10ms), 离线激活按需付费

**Router 角色升级**:
- 在线穿透 (找 gateway) 和离线穿透 (找 home serv) 统一
- `players:{id}` 集合区分"离线"和"不存在"— 新玩家/未注册返回 not_found, 防止为不存在的玩家激活空 Actor

**与在线 Actor 的对比**:
| 属性 | 在线 Actor | 离线 Actor |
|------|-----------|-----------|
| session | 有 (关联 gateway) | null |
| 推送 | ProjectorSystem → Gateway → Client | 无推送, 仅写库 |
| 激活方式 | 登录时 LoadAll (全量, 50-200KB) | 首次 RPC 按需懒加载 (单 BehaviorInfo, ~200B-5KB) |
| 内存占用 | 全量 BehaviorInfo 常驻 (~100KB) | 仅已访问的 BehaviorInfo (~200B-20KB) |
| 业务代码路径 | 相同 | 相同 |
| 持久化路径 | DataStore → Truck → WAL → MongoDB | 完全相同 |

### 5.6 跨进程 RPC (协程化 + 幂等)

```
  A.serv 的 Actor 调 B.serv:

  IEnumerator DoCrossCall(actorId) {
      var req = new RpcRequest { requestId = Guid(), target=B, method="...", args=... };
      _rpc.Send(req);
      yield return new WaitForRpc(req.requestId);  // 挂起 Job, 让出线程, 别的 Actor 跑
      var resp = _rpc.TakeResult(req.requestId);   // 下帧 resume 取结果
      if (resp.Redirect != null) {
          if (req.hops++ > 3) { Fail("redirect loop"); yield break; }
          _rpc.Send(req with { target = resp.Redirect }); yield return new WaitForRpc(...);
      }
      Process(resp);
  }
```

**可靠性契约**:
- **at-least-once + 业务幂等**: 每个 RPC 带 `requestId`,目标侧去重表(窗口 30s,定时清理),重复返回缓存结果
- **重试**: 超时由调用方协程控制,默认 3 次退避重试
- **Redirect**: hop ≤ 3,超出 fail
- **循环防护**: 调用栈深度 ≤ 8;`A→B→A` 第二跳异步化(B 不直接回调 A,A 的协程 yield 等 B 响应)
- **目标不可达**: Router 报 not_found → 调用方返回失败 (玩家不存在)

### 5.7 跨进程事务:冻结-确认模式(替代 2PC)

```
  ① A 冻结道具: A 的 BagBehaviorInfo 标记道具 frozen (BehaviorInfo 字段, 不真正删除)
  ② RPC → trade.serv: TradeRequest(A, items, B, gold)
  ③ trade.serv:
     - RPC → B.serv: 冻结金币 (同冻结语义)
     - 双方冻结成功 → 确认:
        RPC → A.serv: 提交(删除冻结道具, 加金币)
        RPC → B.serv: 提交(删除冻结金币, 加道具)
     - 任一冻结失败 → 解冻:
        RPC → A.serv: 解冻道具 (幂等)
        RPC → B.serv: 解冻金币 (幂等)
  ④ 冻结状态在 BehaviorInfo 中, [Persistent] 写盘可恢复

  ★ 无 2PC 的阻塞与协调者单点
  ★ 冻结是本地操作 (单线程, 无锁)
  ★ 确认/解冻是幂等 RPC (requestId 去重)
  ★ 超时(trade.serv TimerWheel): 冻结超时未确认 → 自动解冻
```

### 5.8 拍卖行

```
  浏览: Redis 缓存直接返回 (不经过 auction.svc 业务线程)

  竞价 (一致性优先):
    auction.svc 业务线程:
      ① 验证出价 > 当前价 → 更新内存 listing → 标记脏
      ② 同步写 Redis (浏览者看到最新价)
      ③ 退回上一个竞价者金币:
         ★ 先写本地 WAL (refund 记录) → RPC → player.serv 退款
         ★ RPC 成功 → 删除 WAL 该记录
         ★ RPC 失败 → WAL 重放补偿 (至少一次语义)
         ★ player.serv 退款接口幂等 (requestId 去重)
      ④ 扣除当前竞价者金币: 冻结模式 (player.serv 本地冻结, 竞价成功才扣除)
      ⑤ 返回 BidOk

  成交 (TimerWheel 到期):
    → RPC → player.serv (卖家): Mail + 金币
    → RPC → player.serv (买家): Mail + 物品
    → 归档 MongoDB, 内存清理
```

### 5.9 数据安全四件套(回滚已删,替代方案)

回滚干掉(行业普遍不做内存业务层事务回滚)。数据安全靠:

| 机制 | 用途 |
|------|------|
| **先校验后执行** | Job 内校验在前改动在后,执行阶段不失败(失败=bug,靠日志+补偿) |
| **冻结-确认** | 跨步骤/跨进程交易(5.7);超时自动解冻 |
| **WAL** | 崩溃恢复,持久化层重放;拍卖退款不丢 |
| **幂等补偿** | 失败显式反向操作(拍卖退款 WAL+幂等 RPC);requestId 去重 |

游戏业务失败 99% 在校验阶段(道具不足/等级不够),此时没改数据,回滚无意义。真正"改一半失败"用四件套覆盖。

### 5.10 级联操作与异步事件总线

**问题**: 一次简单操作触发级联链 — 点赞→任务→经验→升级→属性重算→排行→邮件。若每步触发 IO 懒加载且串联全部同步执行，响应时间爆炸。高并发下 1000 人对同一离线目标操作，串行排队。

**原则**: **局部同步，全局异步。同步写源，异步联级。**

#### 5.10.1 同步/异步分界

```
  A → B 点赞 (RPC):

  同步部分 (A 等待, ≤1 帧):
    ① 校验 (A 未重复点赞)
    ② _store.Get<LikeBehaviorInfo>(B).count++
    ③ Reply Ok()                       ← A 拿到结果, RPC 结束
    ④ InternalEventBus.Publish(PostLiked { B, postId, A })

  异步部分 (不阻塞 A, 后续帧处理):
    PostLiked → QuestSystem 检查任务
    QuestCompleted → ExpSystem 发经验
    ExpFilled → LevelSystem 升级
    LevelUp → AttrSystem 重算
    AttrChanged → CrossServiceEvent → Rank / Mail (跨服务, 完全异步)
```

**硬约束**: RPC 请求在"改源数据 + 校验"完成后立刻 reply。之后的级联不阻塞调用方。调用方关心的是"赞写进去了没"，不是"B 升级了没"。

#### 5.10.2 InternalEventBus — Actor 内事件总线

```
  ┌─────────── B 的 Actor ─────────────────────────┐
  │                                                  │
  │  Frame N:                                        │
  │    RPC(Like) → 校验 → 写 LikeBehaviorInfo        │
  │    → Reply Ok() → Publish(PostLiked)              │
  │                                                  │
  │  Frame N+1: (DrainInternalEvents, 最多 M 个)      │
  │    PostLiked → QuestBehavior.OnPostLiked()        │
  │    → Get<QuestBehaviorInfo> → IO yield → 下帧      │
  │                                                  │
  │  Frame N+2: (QuestBehaviorInfo 已缓存)            │
  │    → quest.progress++                            │
  │    → quest.completed? → Publish(QuestCompleted)   │
  │                                                  │
  │  Frame N+3:                                      │
  │    QuestCompleted → ExpBehavior.OnQuestCompleted  │
  │    → Get<ExpBehaviorInfo> → IO yield → 下帧        │
  │    ...                                           │
  │                                                  │
  │  ★ 每帧每 Actor 处理 ≤ CascadeBudget 个内部事件     │
  │  ★ IO 未命中时 yield, 让出 CPU 给其他 Actor        │
  │  ★ 事件合并: 同帧同类型事件 coalesce                │
  └──────────────────────────────────────────────────┘
```

```csharp
public class InternalEventBus
{
    // actorId → 待处理事件队列
    Dictionary<ulong, Queue<IEvent>> _queues;
    int _cascadeBudgetPerActor = 5;    // 每 Actor 每帧最多处理 5 个级联事件

    public void Publish(ulong actorId, IEvent e) {
        var q = _queues[actorId];
        // ★ Coalesce: 同类型可合并事件 → 合并而非追加
        if (q.Count > 0 && q.Last() is ICoalesceable last && last.CanCoalesce(e))
            last.Merge(e);                       // e.g. PostLiked × 50 → PostLiked {count:50}
        else
            q.Enqueue(e);
    }

    // 帧末由 Stage 调用, 每 Actor 最多 drain CascadeBudget 个
    public void Drain(ulong actorId, int budget);
}

// Behavior 订阅
public class QuestBehavior : Behavior<PlayerBehaviorInfo> {
    [OnInternalEvent]
    void OnPostLiked(PostLikedEvent e) {
        var quest = _store.Get<QuestBehaviorInfo>(e.playerId).activeQuests
                          .Find(q => q.type == QuestType.Likes);
        if (quest != null) {
            quest.progress += e.count;   // coalesce 后批量推进
            if (quest.progress >= quest.target)
                _eventBus.Publish(e.playerId, new QuestCompleted { questId = quest.id });
        }
    }
}
```

**Coalescing 场景**:

```
  1000 人赞离线 B:
    Frame N: 第 1 个赞 → 激活 Actor → 懒加载 LikeBehaviorInfo → yield
    Frame N+1: 数据到位 → count++ → Reply → Publish(PostLiked)
    Frame N+1 同帧: 第 2-1000 个赞排队执行 (Actor 在内存, LikeBehaviorInfo 已缓存)
      → 每个 Reply Ok
      → 每次 Publish(PostLiked) → coalesce 合并 count
    Frame N+2: Drain → 只处理一个 PostLiked{count:1000}
      → QuestBehavior 一次性推进 1000 步 → 检查一次完成

  ★ 赞 1000 次: 第 1 次 ~1ms IO, 后续 999 次纯内存 (每赞 ~μs)
  ★ 级联事件: 处理 1 次而非 1000 次
```

#### 5.10.3 CrossServiceEvent — 跨服务异步

```
  Actor 内事件链末端 → 发跨服务事件:

  LevelUp → AttrSystem 重算完成
          → CrossServiceEvent.Publish(PlayerLeveledUp { playerId, newLevel })
          → NATS / Redis Streams (at-least-once, WAL 兜底)

  Rank.Service 订阅:
    PlayerLeveledUp → 更新排行榜 (异步, 不阻塞 player.serv)

  Mail.Service 订阅:
    PlayerLeveledUp → 生成升级奖励邮件 (异步, 失败 WAL 重放补偿)
```

| 事件类型 | 作用域 | 传输 | 可靠性 |
|---------|--------|------|--------|
| **InternalEvent** | Actor 内 (Behavior→Behavior) | 内存队列, 单线程 | 内存保证, 崩溃丢失可重建 |
| **CrossServiceEvent** | 跨 Service | NATS / Redis Streams | at-least-once, WAL 日志兜底 |

#### 5.10.4 IO 与内存预算

**IO 现实**:

| 操作 | 数据量 | 典型延迟 (NVMe SSD + MongoDB) |
|------|--------|------|
| 懒加载单个 BehaviorInfo | 200B - 5KB | 0.5 - 2ms |
| LoadAll (全量 BehaviorInfo) | 50 - 200KB | 5 - 15ms |
| WAL append (顺序写) | ~100B | 0.1ms (批量 fsync 摊销) |
| Truck 批量写 MongoDB | N × 10KB | 批量摊销 ~0.5ms/文档 |

**离线 Actor 按需加载 vs 在线 LoadAll**:

```
  在线玩家登录: LoadAll → 50-200KB, ~5-15ms → 一次性, 后续零 IO
    → 合理: 在线玩家会用到大部分 BehaviorInfo

  离线被赞: 懒加载 LikeBehaviorInfo → ~200B, ~0.5ms → 只用此一个
    → 如果 LoadAll 50KB → 浪费 99.6% IO + 99.6% 内存
```

**内存预算**:

```
  在线 Actor:   全量 BehaviorInfo 常驻    ~100KB/Actor × 5000 = 500MB
  离线 Actor:   仅已访问的 BehaviorInfo   ~200B-20KB/Actor (按需增长)

  批量离线场景 (全服发邮件, 瞬时 10000 个离线 Actor):
    → 硬上限 MaxOfflineActors (如 2000)
    → 超出则 ActivationRateLimiter (每秒最多激活 N 个)
    → ActorDestroySeconds 到期后释放内存
    → 绝不让离线激活挤占在线玩家的内存和帧预算
```

**级联 IO 自限性**:

```
  级联链每步首次访问新 BehaviorInfo → IO yield → 下帧
  → 级联自然跨越多个帧, 每个 yield 点切到其他 Actor
  → 一个 Actor 的级联从不独占 CPU
  → IO 密集级联 (全未缓存) 5 步 ≈ 5 帧 (~80ms)
  → 内存命中级联 (全已缓存) 5 步 ≈ 同帧 (~μs)
```

#### 5.10.5 配置

```json
{
  "Cascade": {
    "MaxInternalEventsPerActorPerFrame": 5,
    "MaxOfflineActors": 2000,
    "ActivationRateLimitPerSec": 200,
    "CoalescingWindowMs": 16
  }
}
```

---

## 六、主循环

### 6.1 Engine (所有进程通用,单线程)

```
  while (_running):
      try:
          DrainCallbacks()         // MPSC: IO 结果 + RPC 响应 + DB 读完成 → 唤醒挂起的协程
          DrainTimers()            // TimerWheel → 触发定时协程
          DriveCoroutines()        // 推进所有就绪协程一步 (到 yield 点)
          Publish(Frame)           // 帧事件
      catch (Exception ex):
          Log(ex);                 // 异常隔离, 不崩进程
      SleepToNextFrame()           // 精确等帧
  FlushAll()
```

### 6.2 各进程 OnFrame

```
  Gateway:  DrainNetwork · DrainRpcCallbacks · DrainTimer
  Router:   DrainRpcCallbacks · DrainTimer (心跳 TTL)
  player:   DrainRpcCallbacks · DriveCoroutines · ProcessActors · ProjectorSystem · TruckCheck
  club:     DrainRpcCallbacks · ProcessOps · TruckCheck
  chat:     DrainRpcCallbacks · PushMessages · TruckCheck
  auction:  DrainRpcCallbacks · DriveCoroutines · ProcessBids · TruckCheck
  trade:    DrainRpcCallbacks · DrainTimer (超时解冻) · DriveCoroutines
```

### 6.3 ProcessActors (单线程协程交替 + 公平调度)

```
  // 单线程, 所有 Actor 共享一个线程
  foreach actor in _actors.OrderByDescending(r => r.StarvationFrames):
      budget = Clamp(baseBudget + starvationFrames/3, min:5, max:25)
      processed = 0
      while processed < budget && actor.HasJobs:
          job = actor.DequeueJob()
          try:
              job.Execute()         // 同步 Job: 一步完成; 协程 Job: 推进到 yield 点
              if (job.Yielded) {
                  actor.ReenqueueJob(job)   // 协程挂起, 等结果回来下帧 resume
                  break;                     // 切到下一个 Actor (单线程不阻塞)
              }
              job.Complete()        // 回复 RPC / 标记完成
              actor.FlushSends()
          catch (Exception ex):
              // ★ 无回滚 (回滚已删): 异常隔离, Job 失败不污染其他 Actor
              Log(ex);               // 记录, 人工补偿
              job.Fail(ex)
              actor.ClearSends()
          processed++

          if (frameTimeBudgetExceeded) {
              actor.StarvationFrames++;   // 帧预算超时, 剩余下帧, 但记饥饿优先
              break;
          }

      actor.StarvationFrames = actor.HasJobs ? actor.StarvationFrames + 1 : 0
      if actor.StarvationFrames > 60 → Alert + 建议热迁移该 Actor
```

**长尾保护**:单 Actor 单帧 Job ≤ 25;帧预算超时剩余下帧;StarvationFrames > 60 告警+建议迁移。

---

## 七、安全

### Gateway 入口防护

```
  Rate Limit:    每 IP 每秒 N 次 (Token Bucket, Redis 计数器)
  Token 校验:    每个 RPC 携带 session token, Gateway 校验 HMAC
  DDoS 基础:     MaxConnections / MaxPacketSize / ConnectionTimeout
  Service 间通信: 内网 RPC + HMAC 签名 + requestId 去重
```

---

## 八、扩容与 CCU 保持

### 8.1 单实例容量

```
  player.serv (单进程单线程, 单核):
    5000 Actor (保守) → 30MB 内存 → 0.1ms/帧
    可到 20000 Actor → 120MB 内存 → 0.7ms/帧
    ★ 单核上限在帧时间预算和公平调度粒度
    ★ 多核靠多实例 (player.serv N 实例 = N 核)
    ★ 上述数字为预估值, Phase 1 完成后用 benchmark 实测校准
    ★ 离线 Actor 暂用同一内存池; 如批量离线交互(全服发邮件等)瞬时激活大量 Actor,
      需增加 MaxOfflineActors 上限保护, 超出则排队或限流

  Gateway (单进程单线程): 10000 连接 → 5MB 内存 → 2ms/帧
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

  压力: player.serv CPU ~5% (单核) · Gateway CPU ~10% (单核) · Router 可忽略
  MongoDB 分片 10 节点 · 内网 10Gbps 轻松
  ★ 多核 = 多实例, 不靠单进程多线程
```

### 8.3 扩缩容对象

```
  ★ 自动扩缩容: player.serv (有状态, 需热迁移), Gateway (无状态, 秒级)
  ★ 手动扩缩容: club/chat/rank/auction/trade (有状态但单实例够用; 分片后可选自动)
```

### 8.4 Actor 热迁移

**协程可迁移性契约**:
```
  方案 A (推荐): 业务时钟 + 协程可从 BehaviorInfo 重建
    - 引擎维护独立"业务时钟"(Time.Now), 热迁移期间暂停推进, Resume 后补偿
    - 协程禁止捕获外部可变状态, 所有状态进 BehaviorInfo
    - 迁移时: 序列化 [Persistent] + [Projector] 字段 → 目标侧反序列化 → OnEnter 重算派生 →
      从 BehaviorInfo 重新启动协程 (协程入口根据 BehaviorInfo 判断该跑哪些)

  迁移流程:
    单 Actor: Freeze (停 Job, 等协程到 yield 点 ≤1s) → 序列化 BehaviorInfo (~6KB) → RPC → 反序列化 → OnEnter → Resume
    耗时 ~200ms | 并行 50 个/批 × 200ms = 200ms/批
    缩容 5000 Actor: 100 批 ≈ 20s
    玩家体感: < 1s 暂停 (Freeze 期间)

  ★ 协程必须在 yield 点可挂起; 业务时钟保证 Resume 后不因 Time 跳变错乱
  ★ 单线程无线程亲和性问题, 迁移更简单

  迁移一致性:
    新 serv 向 Router 注册 → 覆盖 online:{playerId}
    Gateway 缓存未更新 → 旧 serv 返回 Redirect → 自动重试 (hop ≤ 3)

  目标实例 Crash (迁移中):
    已迁移 Actor 从源实例快照回退 (MongoDB + WAL), 未迁移继续服务
    Controller 取消本次迁移, 选新目标重试
```

### 8.5 扩缩容 = 部署配置

```
  扩容: 启动新实例 → Router 更新 hashring → 热迁移均衡负载 → 完成
  缩容: 标记 draining → 迁移全部 Actor → 进程退出 → Router 移除
  ★ Behavior 代码零改动
```

### 8.6 CCU 保持要点

```
  ① 帧时间预算: 每帧硬上限 (如 50ms), 超时则剩余 Actor 下帧 (公平调度兜底)
  ② 长尾保护: 单 Actor 单帧 Job ≤ 25, StarvationFrames > 60 告警 + 建议迁移
  ③ 零 GC 现实化: 核心路径 < 1KB/帧 (非严格 =0), 用 dotnet-counters 实测
  ④ 突发弹性: 开服/活动峰值 → Controller 自动扩容 (CPU > 70% 持续 30s)
  ⑤ 重连保活: 缓冲期 ActorDestroySeconds 内 Actor 不销毁, resumeToken 跨 Gateway
  ⑥ 无损热迁移: 协程 yield 点挂起, 业务时钟补偿, < 1s 体感
  ⑦ 多核扩展: 单进程单线程, 多核靠多实例 (player.serv N 实例)
```

---

## 九、自动扩缩容 (Controller)

### 9.1 架构
Controller 是运维中控进程。Monitor → Decider → Executor → CloudDriver,完整闭环。

### 9.2 指标采集
```csharp
[RpcService]
public interface IStatsService { [RpcMethod] Task<ServiceStats> GetStats(); }
public class ServiceStats {
    string ServiceType, InstanceId;
    int ActorCount, ActiveActors, TotalQueueDepth, DbConnectionCount;
    float CpuPercent, AvgJobLatencyMs;
    long MemoryMB;  uint FrameNumber;
}
```
Controller 每 5s 拉取全集群 `/stats`。新进程 Ready 检测:连续 3 次心跳 OK → 标记 running → 触发迁移。

### 9.3 决策规则
```
  扩容 (满足任一, 冷却 3min):
    ① 集群 CPU 均值 > 70%, 持续 30s
    ② 任一实例 ActorCount > 20000
    ③ 任一实例 AvgJobLatencyMs > 10ms, 持续 30s
  缩容 (全部满足, 冷却 3min):
    ① 集群 CPU 均值 < 30%, 持续 5min
    ② 所有实例 ActorCount < 上限 50%
    ③ 实例数 > minReplicas (默认 3)
  每次只扩/缩 1 个实例, 30s 观察期后再评估, 防止震荡
  ★ 决策幂等: decisionId, CloudDriver 去重, 防脑裂重复
```

### 9.4 Cloud Driver (可插拔)
```csharp
interface ICloudDriver {
    Task<Machine> Lease(string spec, string startupScript);
    Task Release(string machineId);
    Task<Machine[]> ListMachines();
}
```
换云平台 = 换 Driver。Spot 实例仅适用于 Gateway/Router 无状态服务。

### 9.5 Controller HA (防脑裂)
```
  Controller#1 (主) ← Redis 锁 "controller:leader" (TTL 5s) → Controller#2 (备)
  ★ TTL 5s 缩脑裂窗口; 决策幂等 decisionId; 锁丢失降级; CloudDriver 执行日志持久化可回放
```

### 9.6 DB 连接池
```
  200 player.serv 实例 × 10 MongoDB 连接 = 2000 连接 (安全, 上限 65536)
  如超: mongos 连接复用; 或增加分片
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
  Phase 3: 全自动 + Cloud Driver (推迟, 需 Phase 1-2 跑稳后再上)
```

---

## 十、单实例故障恢复

```
  club.service Crash:
    Controller 检测心跳丢失 → Cloud Driver 启动新实例
    → MongoDB 加载全部 [Persistent] BehaviorInfo → WAL 回放 → Router 注册 → 流量恢复
    → OnEnter 重算派生 ([Projector] only 字段)
    5000 实体 ~5 秒
    恢复期间: Gateway 返回 "服务暂不可用"
```

---

## 十一、零 GC 策略 (现实化)

```
  MessagePack buffer:   ArrayPool<byte>.Shared 复用
  临时集合:             GBLList/GBLDict/TGBLList 从 ObjectPool 租用, 帧末归还
  字符串:               Span<char> + Utf8Formatter 替代 string.Format
  ProjectorPacket:      ObjectCache.Ensure/Set 池化 (1:1 复用 goblin)
  跨进程 RPC:           协程化, 无 TaskCompletionSource 分配
  ★ 核心路径目标: GC.Alloc < 1KB/帧 (非严格 =0)
  ★ 用 dotnet-counters 实测, 而非口号
  ★ 允许: 异常路径、lambda 闭包少量分配 (非核心路径)
```

---

## 十二、配置管理

```json
// appsettings.json → appsettings.Production.json → 环境变量覆盖
{
  "Queen": {
    "Gateway": { "Port": 12801, "MaxConnections": 10000, "RateLimit": {"PerSecond": 50} },
    "Router": { "Port": 10010, "Redis": "redis-sentinel:26379,service=queen-redis" },
    "PlayerService": { "Port": 10020, "MaxActors": 20000, "ActorDestroySeconds": 300, "JobBudgetPerFrame": 5, "FrameBudgetMs": 50 },
    "Persistence": { "MongoDB": "mongodb://mongo:27017/queen", "WalPath": "/data/wal/" }
  }
}
```
`IOptions<T>` 绑定,环境分层。

---

## 十三、测试策略

### 13.1 单元测试
```csharp
[Test]
void Spend_ReducesGold_AndMarksDirty()
{
    var store = new DataStore(new MockDatabase());
    var wallet = new WalletBehavior(store, WalletConfig.Default);
    store.Load<PlayerBehaviorInfo>(actorId).gold = 100;
    wallet.Spend(actorId, 30);
    var p = store.Get<PlayerBehaviorInfo>(actorId);
    Assert.Equal(70, p.gold);
    Assert.Equal(p.gold + p.money, p.total);                    // 派生正确
    Assert.True((p.projectdirtymask & GOLD_BIT) != 0);          // 脏位正确
    Assert.True((p.projectdirtymask & TOTAL_BIT) != 0);
}
```
Behavior + DataStore 两个类独立可测,零 Mock。脏位/派生可验证。单线程下无需考虑并发。

### 13.2 集成测试 + 故障注入
```
  Happy path:
    启动测试集群 → 登录 → OnEnter 算派生 → 客户端请求 → ProjectorSystem 推送
    → 跨服好友(在线+离线) → 下线 → resumeToken 重连 → 热迁移 → 故障恢复

  ★ 故障注入清单 (必测):
    - RPC 半成功: 接收方处理中崩溃 → 调用方超时重试, 幂等去重
    - WAL 写到一半进程崩 → 重启 WAL 回放, 数据完整
    - Redis 主从切换瞬间 → Router 降级本地缓存, 业务不中断
    - Gateway 缓存指向已销毁 serv → Redirect → 重试 → 成功或 hop 超限
    - 离线 Actor 首次 RPC 激活 → 按需懒加载 BehaviorInfo → 执行 → 正常持久化 → 空闲后钝化
    - 热迁移中目标 Crash → 源实例回退, 未迁移继续服务
    - 跨进程事务冻结后超时 → 自动解冻, 双方还原
    - 拍卖退款 RPC 失败 → WAL 重放补偿, 金币不丢
    - Job 执行抛异常 → 异常隔离, 不污染其他 Actor (无回滚, 靠补偿)
```

### 13.3 覆盖率
```
  Queen.Core:        90%+  ([Projector]SG/TGBLList/TGBLDict/DataStore/协程调度 95%+)
  Queen.Rpc/Server:  80%+
  Queen.Network:     80%+  (连接断开/超时/部分写入)
  Queen.Persistence: 80%+  (WAL 损坏恢复/读写/重连)
```

---

## 十四、项目结构 (目标态)

```
Queen.sln
├── src/
│   ├── Queen.Core/            # Engine, Comp, Eventor, Ticker, TimerWheel, MpscQueue, CoroutineScheduler
│   │   ├── Containers/        # GBLList, GBLDict, TGBLList, TGBLDict (1:1 复用 goblin, CollectDiff)
│   │   ├── Scheduling/        # CoroutineScheduler, WaitForRpc, WaitForLoad
│   │   └── EventBus/           # InternalEventBus, ICoalesceable, CrossServiceEvent
│   ├── Queen.Rpc/             # [RpcService] [RpcMethod] [Persistent] [Projector], SourceGen, RpcDispatcher, ProjectorSystem, ProjectorPacket
│   ├── Queen.Network/         # ITransport, TCP/WS/UDP
│   ├── Queen.Persistence/     # MongoRepository, Truck(BatchWriter), WAL, DataStore
│   ├── Queen.Gateway/         # SessionManager, AuthPipeline, RateLimiter
│   ├── Queen.Router/          # ServiceRegistry(Redis), LookupService
│   ├── Queen.Controller/      # Monitor, Decider, Executor, CloudDriver
│   ├── Queen.Server/          # player.serv: Behaviors, Stage, Truck
│   ├── Queen.Club/            # club.service
│   ├── Queen.Chat/            # chat.service + WorldChannel
│   ├── Queen.Rank/            # rank.service
│   ├── Queen.Auction/         # auction.svc
│   ├── Queen.Trade/           # trade.serv (冻结-确认)
│   ├── Queen.Ration/          # HTTP 管理 API
│   ├── Queen.Bot/             # 压测
│   └── Queen.DBObserve/       # DB 观测
├── tests/  (Core / Rpc / Server / Network / Persistence / Integration)
├── configs/
└── analyzers/Queen.Analyzers/  # QN1001 (禁 async, 保护单线程确定性)
```

**现状对照**: 当前仓库 `Queen/`、`Queen.Server/`、`Queen.Compass/`、`Queen.Protocols/` 为旧代码,按目标态重组。`Queen.Protocols/` 被 `[RpcService]` + SourceGen 取代(决策 #17)。容器/Projector/IGBL/ObjectCache 从 goblin 移植(决策 #29)。

---

## 十五、设计决策

### 架构决策

| # | 决策 | 理由 | 状态 |
|---|------|------|------|
| 1 | **Router DNS 模式** | 只寻址不转发; 全直连; 压力极低 | `(目标)` |
| 2 | **Redis Sentinel HA** | 主从 + 故障转移; 不可用降级本地缓存 | `(目标)` |
| 3 | **Behavior/BehaviorInfo 分离** | 单例 System + 数据 Component; 统一 Service 骨架 | `(目标)` |
| 4 | **DataStore 单线程无锁 + 懒加载** | 裸 Dictionary 无锁; Get<T> 未命中挂起协程异步读 | `(目标)` |
| 5 | **[Persistent]/[Projector] 双标志** | 一份结构两标志; 对齐 KBEngine/UE; 不分两份数据 | `(目标)` |
| 6 | **Virtual Actor — 离线交互透明激活** | 借鉴 Orleans; 在线/离线是业务状态非代码路径; Actor 不在内存则按需懒加载; 无 Version/白名单/特殊路径 | `(目标)` |
| 7 | **下线缓冲期** | ActorDestroySeconds 内可重连; resumeToken HMAC 跨 Gateway | `(目标)` |
| 8 | **Gateway 安全入口** | Rate Limit + Token + 连接限制 | `(目标)` |
| 9 | **读写分离** | rank/auction 查询走 Redis 缓存 | `(目标)` |
| 10 | **扩缩容 = 部署配置** | 加实例只改 Router hashring; Behavior 代码零改动 | `(目标)` |
| 11 | **Controller 自动化 + 幂等** | decisionId 防脑裂; TTL 5s | `(目标)` |
| 12 | **单实例故障 WAL 恢复** | MongoDB + WAL 回放; OnEnter 重算派生 | `(目标)` |
| 13 | **Service 间直连** | Router 不碰业务流量 | `(目标)` |

### 实现决策

| # | 决策 | 理由 | 状态 |
|---|------|------|------|
| 14 | **进程内单线程 + 协程交替** | 绝对无锁; 确定性; 与 goblin 同构; 多核靠多进程 | `(目标)` |
| 15 | **禁止 async (QN1001)** | async 引入线程池调度, 破坏单线程确定性 | `(目标)` |
| 16 | **IEnumerator 协程跨帧/跨进程** | 等待时 yield 让出线程; 不阻塞; 单线程交替 | `(目标)` |
| 17 | **干掉 Protocols** | [RpcService] + SourceGen; MessagePack 统一 | `(目标)` |
| 18 | **跨进程 RPC 协程化** | yield 让出线程; 无 tcs; at-least-once + 幂等 | `(目标)` |
| 19 | **冻结-确认替代 2PC** | 本地冻结无锁; 幂等确认; 超时解冻; 无协调者单点 | `(目标)` |
| 20 | **公平调度 + 长尾保护** | 饥饿感知 + 动态预算; 帧预算超时下帧 | `(目标)` |
| 21 | **TimerWheel O(1)** | 替代线性列表 | `(目标)` |
| 22 | **WAL + 重放补偿** | 崩溃恢复; 拍卖退款至少一次语义 | `(目标)` |
| 23 | **IOptions 统一配置** | 环境分层 | `(目标)` |
| 24 | **Behavior 独立可测** | DataStore + Behavior 两类; 零 Mock; 脏位可验证 | `(目标)` |
| 25 | **热迁移业务时钟 + 协程可重建** | 迁移期暂停时钟; 协程从 BehaviorInfo 重建; OnEnter 重算派生 | `(目标)` |
| 26 | **零 GC 现实化** | < 1KB/帧; dotnet-counters 实测 | `(目标)` |
| 27 | **多核靠多进程** | 单进程单线程; N 实例 = N 核 | `(目标)` |
| 28 | **回滚干掉, 四件套替代** | 行业普遍不做内存回滚; 先校验+冻结+WAL+幂等补偿 | `(目标)` |
| 29 | **容器/Projector/IGBL 从 goblin 移植** | 1:1 复用 [Projector]/TGBLList/ProjectorSystem/ObjectCache; 前后端同构 | `(目标)` |
| 30 | **派生事件驱动 (OnEnter/RPC/OnLeave)** | 不 OnTick 全员扫; 不 DependsOn 联动; 不 Rules 算 | `(目标)` |
| 31 | **容器一层扁平 + 替换式** | 深嵌套拍平; 元素改=替换触发脏; 对齐 goblin/KBEngine | `(目标)` |
| 32 | **InternalEventBus — 同步/异步分界** | RPC 只等校验+写源数据即返回; 级联通过 Actor 内事件总线异步推进; 跨服务事件完全异步 | `(目标)` |
| 33 | **离线 Actor 按需懒加载** | 不做 LoadAll; Get<T> 按类型按需加载; 离线激活 IO 代价与操作复杂度成正比,不与玩家数据总量挂钩 | `(目标)` |
| 34 | **事件合并 (Coalescing)** | 同帧同 Actor 同类型事件合并; 1000 赞 → 1 次事件处理而非 1000 次级联 | `(目标)` |
| 35 | **离线内存保护** | MaxOfflineActors + ActivationRateLimiter; 离线激活不挤占在线玩家内存和帧预算 | `(目标)` |

---

## 十六、实施阶段

**依赖关系**: Phase 1-4 关键路径。Phase 1 内部 goblin 容器/Projector 移植是地基的地基。

| 阶段 | 内容 | 依赖 |
|------|------|------|
| Phase 1 | Queen.Core: Engine(退出/异常/帧控), Comp, Eventor, Ticker, TimerWheel, MpscQueue, CoroutineScheduler, **从 goblin 移植 GBLList/GBLDict/TGBLList/TGBLDict/IGBL/ObjectCache + fuzzing** | 无 |
| Phase 2 | Queen.Rpc + SourceGen ([RpcService], [Persistent], [Projector], QN1001, ProjectorSystem, ProjectorPacket) | Phase 1 |
| Phase 3 | Queen.Network (ITransport, TCP/WS/UDP, 连接断/超时) | Phase 1 |
| Phase 4 | Queen.Persistence (MongoRepository, Truck, WAL, DataStore 懒加载) | Phase 1, 2 |
| Phase 5 | Queen.Router (ServiceRegistry+Redis Sentinel, LookupService, Redirect) | Phase 3 |
| Phase 6 | Queen.Gateway (SessionManager, resumeToken, AuthPipeline, RateLimiter) | Phase 3, 5 |
| Phase 7 | Queen.Server (DataStore 懒加载, Behaviors, Stage+协程交替+公平调度, ProjectorSystem 推送, OnEnter/RPC/OnLeave 派生) | Phase 1-4 |
| Phase 8 | Queen.Club, Queen.Chat, Queen.Rank, Queen.Auction | Phase 7 |
| Phase 9 | Queen.Trade (冻结-确认) | Phase 7 |
| Phase 10 | Queen.Controller (Monitor, Decider, Executor, CloudDriver) | Phase 5, 7 |
| Phase 11 | Queen.Ration, Queen.Bot, Queen.DBObserve, Analyzers | Phase 7 |
| Phase 12 | Tests & Polish (80%+ 单元, 故障注入全套, 集成全部关键流程) | 全部 |

**关键路径警示**: Phase 1 的 goblin 容器/Projector 移植 + CoroutineScheduler 是地基,**必须先 fuzzing 覆盖**"TGBLList CollectDiff 索引正确性""协程 yield/resume""projectdirtymask 置位"再进 Phase 2。

---

## 十七、运维与稳定性 (待设计方案)

> **注意**: 以下 13 项为生产级服务端的硬性素养,当前文档偏向业务架构,运维/稳定性基建缺失。本章节列出现状与待定方案,需逐项补设计后进入实施阶段。

### A 组 — 线上稳定性致命缺失 (不补会出事故)

#### 17.1 优雅停机

**现状**: `Engine` 有 `_running` 退出标志,但无 drain 流程。SIGTERM 后直接退出 → 未完成的 Job 丢失、脏数据未 flush、连接硬断开客户端感知差。

**需要的设计**:

```
SIGTERM 信号 → ① 停接新连接 (Gateway 返回 Redirect)
             → ② drain Job 队列 (处理完当前队列)
             → ③ 迁移活跃 Actor (热迁移到其他 serv 实例) 或 保存脏数据
             → ④ flush WAL → Truck 最后一次批量写 MongoDB
             → ⑤ 关闭连接 → 退出进程
```

| 子项 | 说明 |
|------|------|
| drain 超时 | 最长 drain 时间 (如 30s),超时强制退出 |
| Actor 迁移 | 热迁移优先; 超时降级为保存脏数据后退出 |
| 重连引导 | Gateway 在关闭连接时带 `Redirect` 信息,客户端自动重连到新 serv |

#### 17.2 背压 (Backpressure)

**现状**: Job 队列满/推送队列满/Gateway 发送队列满时,文档无策略。旧代码 `Actor.OnRecv` 静默丢消息。

**需要的设计**:

| 场景 | 背压策略 |
|------|---------|
| Actor Job 队列满 | 拒绝新 Job,上游感知 (返回错误码或阻塞上游) |
| 推送队列满 (慢客户端) | 积压超过阈值 → 断开该客户端连接; Gateway 侧限流 |
| Gateway 发送队列满 | 断开最慢的连接; 新连接排队 |
| 全局背压传播 | 下游满 → 上游降速 → 逐级反压 |

需要明确:丢/拒/阻塞的选择边界,以及监控指标 (队列深度告警)。

#### 17.3 服务间熔断/限流

**现状**: Gateway 有 Rate Limit,但服务间 RPC **无熔断器**。`trade.serv` 调 `player.serv`,player.serv 过载 → trade 反复重试 → 雪崩。

**需要的设计**:

```
熔断器状态机:
  关闭 → (连续失败 N 次 / 错误率 > 阈值) → 打开 (短路,直接失败)
  打开 → (等待冷却时间 T) → 半开 (试探一次)
  半开 → 成功 → 关闭; 失败 → 重新打开
```

| 子项 | 说明 |
|------|------|
| 熔断粒度 | 按目标 Service 实例 (player.serv#2) |
| 触发条件 | 连续失败次数 / 超时率 / 响应时间 |
| 半开试探 | 冷却期后放行一次请求,成功则恢复 |
| 限流补充 | 服务间 RPC 也需 Rate Limit (令牌桶),防止一个上游打爆下游 |

#### 17.4 客户端重连风暴

**现状**: 服务器重启/网络抖动后,大量客户端同时重连 → Gateway 瞬间被打爆。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| 客户端退避 | 指数退避 + 随机抖动 (1s → 2s → 4s → 8s, ±25% 随机) |
| Gateway 分批放行 | Token bucket 控制每秒最大新连接数; 超出排队或拒绝 |
| 登录排队 | 在线数达 MaxConnections 后新连接进入等待队列 |
| resumeToken 优先级 | 持有有效 resumeToken 的重连优先处理 |

---

### B 组 — 演进与兼容 (不补会卡迭代)

#### 17.5 协议版本/前后端兼容

**现状**: `[RpcService]` 双端共享接口定义,但**无版本号机制**。客户端老版本 vs 服务端新版本,字段增删改、RPC 方法增删的兼容策略缺失。goblin 热更和服务端发版如何配合未定义。

**需要的设计**:

```
协议兼容规则:
  - 新增 [RpcMethod]: ✅ 向后兼容 (老客户端不调即可)
  - 删除 [RpcMethod]: ❌ 破坏性,需版本号 + 最低版本校验
  - 新增 [Projector] 字段: ✅ 向后兼容 (老客户端忽略未知字段)
  - 删除 [Projector] 字段: ⚠️ 老客户端可能依赖,需评估
  - 修改字段类型: ❌ 破坏性,需版本号
  - 修改 [Persistent] 字段: 属 Schema 演进 (见 17.6)
```

| 子项 | 说明 |
|------|------|
| 协议版本号 | `[RpcService]` 附带 `version` 参数; Gateway 握手时校验客户端版本 |
| 最低兼容版本 | 服务端声明 `MinClientVersion`,低于此版本拒绝连接 (提示更新) |
| 未知字段忽略 | ProjectorPacket 反序列化时忽略未知字段 (MessagePack 原生支持) |
| goblin 热更 | 客户端热更期间,新老版本可能同时存在,淘汰老版本后再下掉兼容代码 |

#### 17.6 数据 Schema 演进/迁移

**现状**: `BehaviorInfo` 加字段 (如新增 `[Persistent("vip", typeof(int))]`),老玩家 MongoDB 文档无此字段。`LoadAll` 时反序列化失败或字段缺失。

**需要的设计**:

```
Schema 版本:
  每个 BehaviorInfo 的 [Persistent] 集合带 SchemaVersion
  MongoDB 文档存储时附带 SchemaVersion

加载迁移:
  LoadAll 时检测 SchemaVersion:
    - 最新版本: 直接反序列化
    - 旧版本: 走 Migration 补字段 (补默认值,不写库) → OnEnter 全量算派生
  ★ 懒迁移: 加载时补默认值,Truck 下次写入写新版 Schema (渐进式,不阻塞上线)
```

| 子项 | 说明 |
|------|------|
| 显式迁移脚本 | 大规模/性能敏感迁移 (新增索引、批量改字段值) 用显式脚本 |
| 回退兼容 | 新字段需保证老客户端/老代码不崩 (默认值安全) |
| 迁移审计 | 记录每次 Schema 变更 (版本号、变更内容、执行时间) |

#### 17.7 发布灰度回滚

**现状**: Controller 管扩缩容,但新代码如何上线没有定义。全量上线新版本出 bug 没有退路。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| 灰度发布 | 新版本先部署 1-2 个实例 (金丝雀),观察指标无异常再全量 |
| 蓝绿部署 | 保留旧版本实例池,新版本部署完毕 → Router 切流量 → 旧版本保活观察期 |
| 回滚发布 | 灰度发现问题 → Router 切回流到旧实例 → 新实例下线 |
| 协议兼容前提 | 灰度期间新老版本并存 (依赖 17.5 协议兼容),Router hashring 需感知实例版本 |

---

### C 组 — 可观测与运维 (不补是黑盒)

#### 17.8 监控告警通道

**现状**: Controller 有决策阈值 (CPU 70%、StarvationFrames>60),但**告警怎么发出去**没有定义 (webhook/IM/值班轮转/告警分级/静默期)。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| 告警通道 | Webhook (企业微信/钉钉/Discord)、邮件、短信 (致命级) |
| 告警分级 | Info(通知) → Warn(关注) → Critical(立即处理) |
| 告警规则 | 阈值 + 持续时间 (防抖动); 如 "CPU>70% 持续 2min" 触发,不是瞬时触发 |
| 值班轮转 | 告警通知到当值人员 |
| 静默期 | 维护窗口期间抑制告警 |
| 聚合/降噪 | 同类告警 N 分钟内合并为一条 |

#### 17.9 日志/链路追踪/指标

**现状**: 有 `Logger`,但缺结构化日志规范、缺 `requestId` 跨进程链路追踪、缺 Prometheus 指标导出。线上出问题时难以定位是哪个请求的哪个步骤出错。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| 结构化日志 | JSON 格式,统一字段: `timestamp, level, service, traceId, requestId, message` |
| 链路追踪 | requestId 贯穿 Gateway → player.serv → RPC 跨进程 (生成 traceId + spanId) |
| 指标导出 | Prometheus metrics: 帧时间、Actor 数、Job 队列深度、GC 频率、RPC 延迟/错误率 |
| 日志级别 | Trace/Debug/Info/Warn/Error/Fatal; 生产环境默认 Info |
| 慢请求日志 | Job 执行超阈值 (如 5ms) 自动记录慢日志 |
| RPC 调用链 | 跨进程 RPC 入参/出参/耗时/状态 记录到 Trace |

#### 17.10 MongoDB HA/备份

**现状**: 文档提到 MongoDB 副本集,但选举细节、备份策略、恢复演练缺失。MongoDB 是 Queen 的持久化根基,单点故障全盘皆输。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| 副本集 | 3 节点 (1 Primary + 2 Secondary),自动故障转移; ReadPreference 读从库分担查询 |
| 全量备份 | 定期 (如每天凌晨) mongodump 全量备份到对象存储 |
| 增量备份 | oplog 增量备份,保留 N 天 |
| 恢复演练 | 定期 (如每月) 在测试环境演练从备份恢复 |
| 监控 | 主从延迟、oplog 窗口、磁盘占用 接入监控告警 |

#### 17.11 死信/人工介入

**现状**: "幂等补偿"失败到死 (WAL 重放 N 次仍失败、冻结-确认双方都解冻失败、退款补偿永久失败) 没有兜底。需要死信队列 + 人工介入入口。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| 死信队列 | 补偿重试 N 次 (指数退避) 后仍失败 → 进入死信队列 |
| 死信存储 | MongoDB 单独集合 `dead_letters`,记录原始请求、失败原因、重试次数、时间 |
| 人工介入 | Queen.Ration 提供 HTTP API: 查看死信列表、手动重试、手动补偿、标记已处理 |
| 告警 | 死信积压超过阈值 → 告警 Critical |
| 幂等重试 | 人工重试保证幂等 (复用 requestId 去重) |

---

### D 组 — 健壮性边界

#### 17.12 协程泄漏/超时

**现状**: 协程 `yield return WaitForRpc(...)` 后,响应丢失 (目标崩/网络断) → 协程**永不 resume**,内存泄漏。同样 `WaitForLoad` DB 读失败也需超时处理。

**需要的设计**:

```
协程超时:
  yield return WaitForRpc(playerServ, req, timeoutMs: 5000)
  超时 → 协程 resume 抛 TimeoutException → 业务 catch 处理

协程取消:
  Actor 销毁/热迁移时,该 Actor 所有等待中的协程取消 (CancellationToken)
  → 协程收到取消信号 → 清理资源 → 退出
```

| 子项 | 说明 |
|------|------|
| 超时默认值 | `WaitForRpc` 默认 5s,`WaitForLoad` 默认 10s,可覆盖 |
| 协程归属 | 每个协程绑定到一个 Actor, Actor 销毁时级联取消 |
| 泄漏检测 | 协程存活超阈值 (如 60s) 告警 (疑似泄漏) |
| 取消传播 | CancellationToken 链式传播到嵌套协程 |

#### 17.13 传输加密

**现状**: 内网服务间使用 HMAC 签名,但**客户端→Gateway 的 TCP/KCP 链路没有加密**。密码、token、玩家敏感数据 (age/gender 等虽不推但登录时有) 可能明文传输。

**需要的设计**:

| 子项 | 说明 |
|------|------|
| TLS (TCP/WS) | 客户端→Gateway 的 TCP/WebSocket 使用 TLS 加密 |
| KCP 加密 | KCP (UDP) 通道自定义加密层 (AES-GCM/ChaCha20-Poly1305) 或 DTLS |
| 证书管理 | 生产环境使用合法 CA 证书; 开发环境自签证书 |
| 重放防护 | 已有时序 nonce (HMAC),加密层额外加序列号防重放 |

---

## 十八、借鉴 Orleans — Virtual Actor 模型对比

> Queen 的离线交互设计在讨论中成型为 Virtual Actor 模型。本章记录与 Orleans 的对比及借鉴项。

### 18.1 Orleans Virtual Actor 核心概念

```
Orleans Grain:
  - Grain 永久"逻辑存在"，调用方不关心是否在内存
  - 首次调用 → silo 自动 Activate (ReadStateAsync)
  - 空闲 → DeactivateOnIdle (WriteStateAsync)
  - 无"在线/离线"概念 — grain 始终可寻址
  - Grain Directory 负责 grain→silo 映射
```

### 18.2 Queen vs Orleans 对照

| 概念 | Orleans | Queen | 差异 |
|------|---------|-------|------|
| 虚拟实体 | Grain | Actor | 相同 |
| 寻址 | Grain Directory | Router + hashring | 相同思路,Queen 用 Redis |
| 激活 | 首次调用自动 Activate (LoadState) | 首次 RPC 按需懒加载 Get<T> | 相同思路, Queen 更细粒度 |
| 钝化 | DeactivateOnIdle | ActorDestroySeconds 后销毁 | 相同 |
| 持久化 | OnActivate/OnDeactivate | Truck + WAL | Queen 批量写更高效 |
| 在线/离线 | **无此概念** | **业务层有** (session 有无) | Queen 保留此概念用于推送判断 |
| 单线程 | 单 grain 单线程 (Turn-Based) | 整个进程单线程 | Queen 更激进 |
| 代码路径 | 统一 | 统一 | 相同 |

### 18.3 Queen 直接借鉴的设计

| 借鉴项 | Orleans 来源 | Queen 落地 |
|------|-------------|-----------|
| **Actor 永久可寻址** | Grain 始终存在,不区分在线/离线 | Router 离线也返回 serv 地址; `players:{id}` 区分不存在 |
| **激活对调用方透明** | `GrainFactory.GetGrain<>().Method()` 调用方不关心激活 | `Router.Lookup() → RPC` 调用方不关心目标是否在内存 |
| **单一代码路径** | 所有 grain 调用走同一路径 | Behavior 不区分在线/离线,DataStore→Truck→WAL 统一 |
| **空闲钝化** | DeactivateOnIdle | ActorDestroySeconds (在线离线共用同一 TTL) |
| **无 Version/乐观锁** | Turn-Based Concurrency 天然无竞态 | 单进程单线程天然无竞态,Version 已删除 |
| **单线程执行保证** | Single-Threaded Execution (每个 grain) | 整个进程单线程 (所有 Actor) |

### 18.4 Queen 故意保留的差异

| 差异 | Orleans | Queen | 理由 |
|------|---------|-------|------|
| **在线/离线业务状态** | 无 | 有 (session 有无) | 游戏需要区分"该不该推送";在线有 session→推送,离线无 session→仅写库 |
| **推送** | Streaming (显式订阅) | ProjectorSystem (脏位自动推送) | 游戏业务逻辑不需要显式订阅,脏位图+帧末增量推送更贴合 |
| **线程模型** | 每个 silo 多线程, grain 按 Turn 串行 | 整个进程单线程 | Queen 对齐 goblin; 绝对无锁; 确定性 |
| **存储** | 任意 storage provider | MessagePack + MongoDB + WAL | 游戏数据访问模式固定,不需要 ORM 抽象 |

### 18.5 为什么不是完全照搬 Orleans

- **游戏需要推送**: Orleans Streaming 是通用方案,Queen 的 ProjectorSystem 是游戏专用(脏位图+增量差量),更省带宽且与 goblin 同构
- **游戏不需要多线程**: Orleans 的 silo 多线程调度适合通用服务,但 Queen 的绝对单线程带来确定性、无锁、与 goblin 客户端同构
- **游戏有明确的"在线/离线"业务差异**: 玩家离线不应该收到推送,但数据仍然可以被修改。Orleans 没有这个区分,Queen 把它建模为 Actor 的 session 字段

### 18.6 删除的设计 (与旧方案告别)

| 删除项 | 旧方案 | 删除原因 |
|------|--------|---------|
| `[OfflineWritable]` | 白名单标记哪些 BehaviorInfo 可离线写 | 所有 BehaviorInfo 都可离线写(激活 Actor 后走正常路径),标记无意义 |
| `Version` 字段 | 乐观锁版本号,离线 CAS 用 | 单 Actor 单线程,不存在并发写冲突 |
| 离线直接 DB 写路径 | `db.Load<T>() → CAS → WAL → MongoDB` | 统一为按需懒加载→Behavior 执行→Truck→WAL |
| "离线 merge" 逻辑 | 登录时合并 WAL 条目 (按 Version 仲裁) | WAL 条目在 Actor 被激活时已正常消费,登录时无需额外合并 |

---

## 变更记录

| 版本 | 日期 | 变更 |
|------|------|------|
| v0.1 | (初版) | 原始架构设计 |
| v0.2 | 2026-07-29 | OpLog 单源双视图; 并发 Actor→线程绑定; RPC 协程化; 2PC→冻结-确认; 拍卖 WAL 退款; 离线白名单; 热迁移业务时钟; Controller 幂等; 零GC<1KB/帧 |
| v0.3 | 2026-07-29 | 线程模型回归单线程+协程交替(撤回多线程); Entity→Actor 命名统一; 多核靠多进程 |
| v0.4 | 2026-07-30 | **数据/同步模型重写**: ① 删除 OpLog/SyncDiff/RollbackLog/oldVal/回滚(回滚干掉,决策#28); ② [SyncField]→对齐 goblin class 级 [Projector]+新增 [Persistent] 双标志(决策#5/#29),类体空 SG 生成,对齐 KBEngine/UE; ③ 脏只推送不回滚,1:1 复用 goblin projectdirtymask/TGBLList.CollectDiff/ProjectorSystem/ProjectorPacket/IGBL/ObjectCache; ④ 派生事件驱动 OnEnter/RPC/OnLeave(决策#30,不 OnTick/不 DependsOn/不 Rules); ⑤ 容器一层扁平+替换式(决策#31,深嵌套拍平,对齐 goblin/KBEngine); ⑥ 写盘 MongoDB 文档整体序列化+Truck(不拆子表); ⑦ 数据安全四件套(先校验/冻结-确认/WAL/幂等补偿); ⑧ ProcessActors 删回滚改异常隔离; ⑨ 热迁移 OnEnter 重算派生; ⑩ **新增第十七章:运维与稳定性待设计方案 (优雅停机/背压/熔断/重连风暴/协议兼容/Schema迁移/灰度发布/监控告警/链路追踪/MongoHA/死信/协程泄漏/传输加密 13 项)** |
| v0.5 | 2026-07-30 | **离线交互 → Virtual Actor 模型 (借鉴 Orleans)**: ① 删除 `[OfflineWritable]` 白名单(所有 BehaviorInfo 均可离线激活); ② 删除 `Version` 乐观锁(单 Actor 单线程无竞态); ③ **5.5 节完全重写**:离线交互 = 透明 Actor 激活,行为与在线完全相同,单一代码路径(DataStore→Truck→WAL); ④ Router 离线也返回 serv 地址(新增 `players:{id}` 集合区分"离线"vs"不存在"); ⑤ 删除离线直接 DB 写路径和登录 WAL merge; ⑥ 新增 `MaxOfflineActors` 上限保护; ⑦ **新增第十八章:Orleans Virtual Actor 模型对比** |
| v0.6 | 2026-07-30 | **级联操作与异步事件总线**: ① **新增 5.10 节**: 同步/异步分界(RPC 只等校验+写源数据,级联异步); ② **InternalEventBus**: Actor 内 Behavior→Behavior 事件通信, 每帧级联预算; ③ **Event Coalescing**: 同类型事件合并(1000 赞→1 次事件处理); ④ **CrossServiceEvent**: 跨服务异步事件(NATS/Redis Streams),Rank/Mail 不阻塞 player.serv; ⑤ **离线激活改为按需懒加载**(不做 LoadAll): IO 和内存代价与操作复杂度成正比, 单个 BehaviorInfo ~200B-5KB, ~0.5-2ms; ⑥ 新增 MaxOfflineActors + ActivationRateLimiter 保护; ⑦ 新增决策 #32-#35 |
