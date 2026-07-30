# Queen 游戏服务端架构方案

> **版本**: v1.2 · **日期**: 2026-07-30
> **状态**: 目标态设计, 唯一真理。当前仓库代码为旧实现, 已废弃, 将按本文档从头实现。

---

## 一、核心哲学

**多进程, 进程内单线程。IO 异步, 业务同步。Actor 永久可寻址, 激活透明。**

### 1.1 核心原则

| 原则 | 说明 |
|------|------|
| **进程内单线程** | 整个进程业务层只有一个线程; 所有 Actor 协程交替执行; 绝对无锁; 多核靠多进程 |
| **IO 异步 offload** | 网络收发、DB 读写走 OS 线程池, 结果通过 MPSC 队列回业务线程 |
| **业务逻辑同步** | 所有业务方法为 `void` 或返回纯值; 禁止 `async` (QN1001) |
| **协程即调度** | Actor Job 在单线程上协程交替; 等待(定时/DB 读/RPC 响应)时 yield, 调度器切到下一个 Actor |
| **Virtual Actor** | Actor 永久可寻址 (借鉴 Orleans)。在线/离线是业务状态 (有无 session), 不是代码路径。Actor 在内存则直接调, 不在则创建壳 + 按需 Get<T> 懒加载 |
| **Behavior/BehaviorInfo 分离** | Behavior = System (单例逻辑, 无状态以利热迁移); BehaviorInfo = Component (纯数据, 类体空, SG 生成) |
| **[Persistent]/[Projector] 双标志** | 一份 BehaviorInfo 结构两个独立标记。写盘用 `[Persistent]`, 推送用 `[Projector]`。 对齐 KBEngine PERSISTENT/CLIENT、UE SaveGame/Replicated |
| **脏推送, 回滚不推送** | dirty 只用于增量推送。Job 失败 → Rollback() → 清 dirty, 客户端不可见。Commit → 进 ProjectorSystem |
| **派生事件驱动** | 派生字段在 OnEnter(全量)/RPC(增量)/OnLeave(清理) 算, 不 OnTick 全员扫 |

### 1.2 非目标

| 不做 | 理由 |
|------|------|
| 客户端预测/状态插值 | 属 goblin 客户端职责 |
| 实时语音/视频 | 走专用媒体服务 |
| Lockstep 帧同步 | 本框架面向异步业务 (背包/邮件/公会/交易) |
| 通用 ORM | 游戏数据访问模式固定 (MessagePack + MongoDB) |
| 分布式事务 2PC | 用冻结-确认模式替代 (见 6.7) |
| 单进程多线程 | 多核靠多进程; 单进程多线程引入锁/竞态, 与 goblin 不同构 |
| 深嵌套容器 | 拍平成复合 key Dict 或拆 BehaviorInfo |

### 1.3 IO / 业务边界

```
                    ┌──────────────────────────────────────┐
                    │         IO 层 (允许 async/Task)       │
                    │  Queen.Network / Queen.Persistence   │
                    │  OS 线程池: 收发 / DB 读写             │
                    └──────────────┬───────────────────────┘
                                   │ MPSC Queue (唯一跨线程接触点)
                    ┌──────────────▼───────────────────────┐
                    │  业务层 (进程内单线程, 禁止 async)    │
                    │  所有 Actor 唯一业务线程, 协程交替     │
                    │  Behavior 方法 → void                │
                    │  Coroutine (跨帧/跨进程 yield)        │
                    └──────────────────────────────────────┘
```

---

## 二、进程拓扑

```
                         CLIENT
            TCP / UDP(KCP) / WebSocket
                          │
                          ▼
   ┌──────────────────────────────────────────────┐
   │              GATEWAY (N 实例)                  │
   │  连接管理 · 认证 · session · resumeToken       │
   └──────────────────────┬───────────────────────┘
                          │
                          ▼
   ┌──────────────────────────────────────────────┐
   │              ROUTER (2~N 实例, HA)             │
   │  DNS 模式: 只回答"X 在哪", 不转发             │
   └──────────────────────┬───────────────────────┘
                          │  ★ 全直连
   ┌──────────────────────┼───────────────────────┐
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
MongoDB + Redis

                    ┌─────────────────────┐
                    │    CONTROLLER       │
                    │  (1 实例, 主备)      │
                    └─────────────────────┘
```

**每个进程内部单线程, 多核靠多实例。Router 管寻址, Controller 管扩缩容, 职责分离。所有 Service 直连。**

---

## 三、Router — DNS 模式

### 3.1 数据模型

```
Redis (Sentinel/Cluster, HA):
  services:{type}          → Set<instanceAddr>
  services:{type}:hashring → SortedSet<slot, instanceAddr>
  online:{playerId}        → {servAddr, gatewayAddr}  (TTL 5s)
  players:{playerId}       → 1  (永久, 首次登录写入, 区分"离线"vs"不存在")

Router 本地缓存: 500ms 刷新
Gateway/Service 本地缓存: 30s TTL
```

### 3.2 寻址

```
player 类型提供两个 API:

Seek(actorId):
  查 online:{actorId}
    → 命中 → {serv, gateway, status:"online"}
    → 未命中 → {status:"offline"}              ← 不返回 serv, 不激活

SeekDeep(actorId):
  查 online:{actorId}
    → 命中 → {serv, gateway, status:"online"}
    → 未命中 → 查 players:{actorId}
        → 存在 → hashring → {serv, gateway:null, status:"offline"}  ← 返回 serv, 允许激活
        → 不存在 → {status:"not_found"}                          ← 从未注册

非 player 类型 (club/chat/rank/auction/trade):
  Seek → hashring → 实例地址
```

**Seek vs SeekDeep 的使用边界**:

| API | 语义 | 适用场景 | 离线行为 |
|------|------|------|------|
| `Seek` | 只找在线 | 聊天/组队/实时交互 | 返回 offline, 调用方自行处理 (发离线消息/拒绝) |
| `SeekDeep` | 穿透激活 | 好友/邮件/交易/公会 | 返回 home serv, 目标 serv 创建壳 Actor + 懒加载 |

**调用方示例**:

```
Chat.SendDM(A, B, msg):
  r = Router.Seek(B)
  if r.status == "online":  RPC → serv, 实时送达
  if r.status == "offline": StoreOfflineMessage(B, msg)   ← 不激活 Actor

Friend.Add(A, B):
  r = Router.SeekDeep(B)
  if r.status == "not_found": return Error("玩家不存在")
  RPC → serv  ← 在线也好离线也好，目标 serv 负责确保数据写入
```

### 3.3 缓存失效与重定向

```
Actor 迁移后 Gateway 缓存可能指向旧 serv:
  Gateway → 旧 serv → Redirect {newServ} → Gateway 更新缓存 → 重试
  ★ 重试上限 3 次, 超出返回失败
  ★ 每个 Redirect 携带 hop 计数, Service 检测 hop > 3 直接拒绝
```

### 3.4 Redis 高可用

Sentinel 主从 + 自动故障转移。完全不可用时 Router 从本地缓存应答 (30s 内可用)。

---

## 四、统一 Service 骨架

**所有业务进程共用一个骨架。player.serv 不特殊。每个进程内部单线程。**

### 4.1 术语

| 概念 | 是什么 |
|------|--------|
| **BehaviorInfo** | Component, 纯数据; class 级 `[Persistent]`/`[Projector]` 特性声明, 类体空, SG 生成一切 |
| **Behavior** | System, 单例逻辑, 对应一种 BehaviorInfo, 无状态 (利热迁移) |
| **DataStore** | 存储, `Get<T>` 懒加载; 单线程访问, 裸 Dictionary 无锁 |
| **[Persistent]** | 声明写盘字段 → SG 生成 MessagePack 持久化序列化 |
| **[Projector]** | 声明推送字段 → SG 生成 backing field + 属性 + `projectdirtymask` 脏位 + `IProjectable` (对齐 goblin) |
| **projectdirtymask** | ulong 位图, 每个 [Projector] 字段一位, set 时置位 |
| **GBLList / GBLDict** | 持久化容器 (纯写盘) |
| **TGBLList / TGBLDict** | 推送容器, 带脏追踪。不暴露 `[]` 索引器, 只提供 `Get/Set/Add/RemoveAt`; `CollectDiff` 输出 added/removed 索引差量 |
| **Actor** | 数据归属单元, 走 Stage 协程交替调度 |

### 4.2 BehaviorInfo — class 级特性声明, 类体空

```csharp
[Persistent("gold",  typeof(int))]  [Projector("gold",  typeof(int))]
[Persistent("money", typeof(int))]  [Projector("money", typeof(int))]
[Projector("total", typeof(int))]                           // 派生: 只推不写
[Persistent("name", typeof(string))] [Projector("name", typeof(string))]
[Persistent("age", typeof(int))]                             // 只写不推 (敏感)
[Persistent("items", typeof(GBLList<Item>))] [Projector("items", typeof(TGBLList<Item>))]
public partial class PlayerBehaviorInfo : BehaviorInfo { }   // 类体空
```

字段组合: `[Persistent]+[Projector]` 写+推 / `[Persistent]` only / `[Projector]` only / 都不标=内部状态。

### 4.3 Behavior — System 单例

```csharp
public class WalletBehavior : Behavior<PlayerBehaviorInfo>
{
    [RpcMethod]
    public void Spend(ulong actorId, int cost)
    {
        var p = _store.Get<PlayerBehaviorInfo>(actorId);
        if (p.gold < cost) return;         // 先校验
        p.gold -= cost;                     // 改源 → SG setter 置 projectdirtymask
        p.total = p.gold + p.money;         // 增量算派生
    }
    // 跨 Behavior: _store.Get<BagBehaviorInfo>(id).items.Add(x)
}
```

### 4.4 SourceGen 生成

```csharp
partial class BagBehaviorInfo : IProjectable
{
    // === SG 生成: 标量字段 ===
    private int _bagLevel;
    private int _bak_bagLevel;
    private bool _dirty_bagLevel;

    public int bagLevel {
        get => _bagLevel;
        set {
            if (!_dirty_bagLevel) { _bak_bagLevel = _bagLevel; _dirty_bagLevel = true; }
            if (_bagLevel != value) { _bagLevel = value; projectdirtymask |= BAGLEVEL_BIT; }
        }
    }

    // === SG 生成: 容器字段 ===
    // 不暴露 [] 索引器, 不返回元素引用 — 杜绝原地改逃逸
    private List<Item> _items;
    private List<Item> _bak_items;
    private bool _dirty_items;

    public Item Get(int i) => _items[i];              // 只读副本, 不触发备份
    public int Count => _items.Count;

    public void Set(int i, Item val) {
        if (!_dirty_items) { _bak_items = DeepCopy(_items); _dirty_items = true; }
        _items[i] = val;                              // 改内存
        projectdirtymask |= ITEMS_BIT;
        // TGBLList: addedIndices.Add(i)
    }

    public void Add(Item val) {
        if (!_dirty_items) { _bak_items = DeepCopy(_items); _dirty_items = true; }
        _items.Add(val);
        projectdirtymask |= ITEMS_BIT;
        // TGBLList: addedIndices.Add(Count - 1)
    }

    public void RemoveAt(int i) {
        if (!_dirty_items) { _bak_items = DeepCopy(_items); _dirty_items = true; }
        _items.RemoveAt(i);
        projectdirtymask |= ITEMS_BIT;
        // TGBLList: removedIndices.Add(i)
    }

    // Rollback: 遍历 _dirty_*=true 的字段, 从 _bak_* 恢复
    // Commit:  清空所有 _bak_*, 重置 _dirty_*=false
}
```

- 标量字段 setter 首次写 → 拷贝值到 `_bak_`, 标 `_dirty_`
- 容器字段 Set/Add/RemoveAt 首次调用 → 深拷贝一层到 `_bak_`, 标 `_dirty_`
- Get(i) 返回只读副本, 不触发备份
- Rollback → 遍历 `_dirty_*=true` 的字段, 从 `_bak_` 反向恢复, 清 `projectdirtymask`
- Commit → 丢弃 `_bak_*`, 清 `_dirty_*`, dirty 进 ProjectorSystem + Truck

### 4.5 DataStore — 单线程无锁 + 懒加载

```csharp
class DataStore {
    T Get<T>(ulong actorId) where T : class, new();  // 懒加载: 命中→返回; 未命中→异步DB读→yield→下帧resume
    void LoadAll(ulong actorId);                      // 全量加载 (在线登录)
    void Save(ulong actorId);                         // 标记脏 → Truck 批量写 MongoDB
}
```

**Get<T> 懒加载时序**: 命中内存 → O(1) 返回。未命中 → 标记 Job Yield → 发起异步 DB 读 (IO 线程) → 挂起 Job → 调度器切到下一个 Actor → MPSC 回调 → 写入索引 → 下帧 Job resume → 命中。

空壳 Actor (离线激活) 的 BehaviorInfo 全部从 `Get<T>` 按需加载。在线登录用 `LoadAll` 一次性加载全部类型。

### 4.6 数据温度 (BehaviorInfo 粒度)

```
Hot   全量 BehaviorInfo 常驻    ~1.5MB/Actor   在线玩家活跃
Warm  部分 BehaviorInfo 驻留    ~400KB/Actor   在线空闲 / 离线重度交互
Cold  空壳                      ~1.5KB/Actor   离线激活初始态

升温: Cold → Get<T> → Warm → 多次 Get<T> → Hot
降温: Hot → idle 30s → 卸载未引用 BehaviorInfo → Warm → idle → 钝化
```

不做字段级: BehaviorInfo 已按领域细拆, 离线交互通常只碰 1-2 种类型, 字段级额外复杂度的收益可忽略。

---

## 五、Service 拆分原则

```
满足任一 → 独立 Service:
  ① 共享可变状态 (ClubInfo, AuctionListing)
  ② 全局视角 (排行榜, 世界频道)
  ③ 中立协调 (冻结-确认交易)
  ④ 事务模型不同 (聊天/拍卖不需要持久化事务)

不满足 → 在现有 Service 加 Behavior + BehaviorInfo
```

| 业务 | 归属 | 拆出原因 |
|------|------|----------|
| 背包/邮件/好友/任务 | player.serv | 私有数据 |
| 公会 | club.service | 共享可变 |
| 聊天/世界频道 | chat.service | 全局视角, 无事务 |
| 排行榜 | rank.service | 全局视角 |
| 拍卖行 | auction.svc | 全局+共享+无事务 |
| 交易 | trade.serv | 中立协调 |

---

## 六、关键流程

### 6.1 登录

```
Client → Gateway:
  ① 验证账号 + Rate Limit
  ② 签发 sessionId + resumeToken (HMAC, 5min)
  ③ Router → hashring 算 home serv → serv 地址
  ④ RPC → player.serv:
     DataStore.LoadAll(id) → new/restore → OnEnter 算派生
     创建 Actor{actorId, session} → 加入 Stage
     Router 注册 online:{id} = {serv, gateway}
     首次登录: SET players:{id} = 1
  ⑤ Gateway 缓存 playerId → serv (TTL 30s)
  ⑥ 发 resumeToken 给 Client
```

### 6.2 客户端 RPC

```
Client → Gateway → RPC 直连 player.serv → 封装 Job:
  Coroutine Handle(actorId, cost):
    var p = _store.Get<T>(actorId); if (p==null) yield return null
    _behavior.Spend(actorId, cost)
    Reply(Ok())
    // ProjectorSystem 帧末统一推送脏字段

★ 无 TaskCompletionSource, 无轮询
★ 响应延迟 = Job 排队 + 1 帧 (< 5ms)
★ 大多数纯同步 Job 一步完成
```

### 6.3 增量推送 (ProjectorSystem)

```
帧末:
  foreach proj in projectables:
    if (proj.projectdirtymask == 0) continue     // 99% 跳过
    packet = {actor, behaviorinfotype, fieldmask, values}
    CollectContainerDiffs(info, mask, packet)     // TGBLList.CollectDiff
    proj.ClearProjectDirty()

  → ProjectionPipeline (Rules 裁剪/格式化, 无状态) → Transport → Client
```

### 6.4 下线与重连

```
下线: Gateway 断连 → Router 删 online:{id}
      → Actor 标记 session=null, 保留 ActorDestroySeconds (300s)
      → 缓冲期后: OnLeave → Save → 销毁

重连: Client 发 resumeToken (5min 有效)
      → Router 查: 缓冲期内 → 旧 serv; 已销毁 → 重登录
      → Reconnect → 更新 session → MarkAllDirty 全量推 → 新 resumeToken

★ 缓冲期内 Actor 不销毁, 其他玩家可正常 RPC 交互
★ resumeToken 跨 Gateway 有效
```

### 6.5 离线交互 — Virtual Actor 模型

**在线/离线是业务状态 (session 有无), 不是代码路径。**

```
Friend.Add(A, B):
  ① Router.SeekDeep(B)
     → {serv, gateway, "online"}  /  {serv, null, "offline"}  /  "not_found"→拒绝

  ② RPC → player.serv#X:
      Actor 在内存? → 直接执行
      Actor 不在? → 创建壳 (session=null) → Get<FriendBehaviorInfo>(B) 懒加载
                  → IO yield → 下帧 resume → friends.Add(A)
      Reply Ok()

  ③ Actor 缓冲期内保留, 后续请求零 IO
  ④ 有 session → 推送; 无 session → 仅写库

★ 同一代码路径, 无 Version/白名单/CAS
★ 离线激活不做 LoadAll — 单个 BehaviorInfo 按需加载 (~200B-400KB, 0.5-2ms)
```

### 6.6 跨进程 RPC (协程化 + 幂等)

**RPC.Fetch<T> — 只读查询**:

```csharp
// 读远程 Actor 的 BehaviorInfo, 返回快照, 不修改远程数据
Coroutine Handle(actorId, targetId):
  var info = yield return Rpc.Fetch<BagBehaviorInfo>(targetId);
  if (info.items.Any(item => item.Id == needId)) { ... }
  // info 是快照 — 对它的修改不会写回远程 Actor
```

**RPC.Call — 写操作 (目标 Actor 自己执行)**:

```csharp
// 写必须让远程 Actor 在它自己的 Job 内执行
Coroutine Handle(actorId, targetId):
  yield return Rpc.Call(targetId, "Bag.ExchangeItem", myId, itemA, itemB);
  // 远程 Actor 进入 BeginJob → 修改自己的数据 → Commit/Rollback
```

**可靠性**:
- at-least-once + requestId 去重 (窗口 30s)
- 超时重试 3 次 (退避)
- Redirect hop ≤ 3, 调用栈深度 ≤ 8
- 目标不存在 → 调用方返回失败

### 6.7 冻结-确认交易 (替代 2PC)

```
① A 冻结道具 (BagBehaviorInfo.frozen, 不真删)
② RPC → trade.serv → RPC → B 冻结金币
③ 双方冻结成功 → A 提交+解冻, B 提交+解冻
   任一失败 → 双方解冻 (幂等 RPC)
④ trade.serv 交易状态 [Persistent] 写盘
   ★ trade.serv 崩溃 → 重启后扫描未完成交易 → 超时的自动解冻, 未超时的继续等
⑤ 超时 (TimerWheel) → 自动解冻
```

### 6.8 拍卖行

```
浏览: Redis 缓存直接返回
竞价:
  ① 验证出价 > 当前价
  ② 同步写 Redis
  ③ 退上次竞价者: 写 pendingRefunds ([Persistent]) → RPC → 成功 → 删 pending → 失败 → 每帧重试
  ④ 冻结本次竞价者金币
  ⑤ BidOk

成交 (TimerWheel): → Mail + 物品 → 归档 MongoDB
```

### 6.9 Job 级字段快照回滚

**SG 为每个字段生成 `_bak_` + `_dirty_` 标记。首次写触发备份, Job 失败 → 反向恢复。**

```
Job 执行:
  actor.BeginJob()

  // 改标量
  p.bagLevel = 5        → SG: _dirty_bagLevel? → 否 → _bak_bagLevel = 旧值 → 标脏 → 写入
  // 改容器
  p.items.Add(newItem)  → SG: _dirty_items?  → 否 → _bak_items = DeepCopy(items) → 标脏 → 调用 Add

  yield Rpc(toServ, toId) → 超时!
  → actor.Rollback()      → bagLevel = _bak_bagLevel; items = _bak_items → 清 projectdirtymask

  actor.Commit()           → 清空所有 _bak_*, 重置 _dirty_* → dirty 进 ProjectorSystem + Truck
```

**约束**:

```
① Job = 原子边界。Commit 之后才允许产生外部副作用 (InternalEvent, RPC回复, 推送)
   业务人员不应该在 Job 内手动 Publish 事件 — Commit 时统一发布

② 一个 Job 只写自己的 Actor。跨 Actor 写走 Rpc.Call (远程 Actor 自己的 Job 内执行, 独立 BeginJob→Commit/Rollback)。
   ✅ p.gold = 70; p.total = p.gold + p.money
   ✅ yield Rpc.Call(target, "Bag.Add", item)           // 远程 Actor 自己 Commit
   ❌ yield Rpc.Fetch<BagInfo>(target) → 拿到快照后本地改 → 改的是副本, 不生效

③ 禁止循环 RPC: A→B→A → A 线程被自己的 Job 占着永远等不到 B 的回调 → 死锁
   → 业务设计阶段杜绝 (hop ≤ 3, 栈深 ≤ 8 兜底检测)
```

**容器深拷贝: 一层**:

```
p.items (List<Item>): 深拷贝 list 本身 + 拷贝每个 Item 元素
  如果 Item 内部有嵌套集合: 只拷贝引用, 不递归
  → 嵌套容器已经被"一层扁平"约束(1.2)禁止 — 不应该存在
```

**正常 Commit 路径**:

```
Job 执行完, 无异常, 无超时 → Commit():
  ① 丢弃所有 _bak_*, 清 _dirty_* 标记
  ② 发布挂起的 InternalEvent (6.10 的异步级联起点)
  ③ dirty (projectdirtymask + TGBLList diff) → ProjectorSystem 帧末推送
  ④ Truck 攒批 → 异步写入 MongoDB
  ⑤ Job 标记 Complete → Reply Ok()
```

### 6.10 级联操作与异步事件总线

**原则: 局部同步, 全局异步。同步写源, 异步联级。**

```
A → B 点赞:

同步 (A 等待, ≤1 帧):
  校验 → p.count++ → Reply Ok() → Publish(PostLiked)

异步 (后续帧, B 的 Actor 内):
  PostLiked → QuestSystem 检查 → QuestCompleted → ExpSystem
  → LevelUp → AttrSystem → CrossServiceEvent → Rank / Mail (跨服务异步)
```

**InternalEventBus — Actor 内 Behavior→Behavior 通信**:
- 每 Actor 独立事件队列, 单线程无锁
- 每帧每 Actor 处理 ≤ CascadeBudget 个事件 (默认 5)
- 事件合并 (Coalescing): 同帧同类型事件合并, 1000 赞 → 1 次 PostLiked{count:1000}

**CrossServiceEvent — 跨服务异步** (NATS / Redis Streams, at-least-once):
- Rank/Mail 等服务订阅事件, 完全异步处理

### 6.11 数据安全

| 机制 | 场景 |
|------|------|
| **先校验后执行** | 参数不合法 → 不改数据提前返回 |
| **Job 级字段快照回滚** | 同步流程中跨进程 RPC 超时/异常 → Rollback() |
| **冻结-确认** | 跨 Actor 分布式交易; 中间态 [Persistent] 写盘可恢复 |
| **幂等补偿 + 持久化重试** | 异步跨服事件已发出无法撤回 → 反向操作; [Persistent] 重试列表 |

MongoDB 是唯一持久化来源。进程崩溃 → 丢失最后一次 Truck flush 后的数据 (~200ms), 对游戏可接受。不做 WAL。

---

## 七、主循环

### 7.1 Engine

```
while (_running):
  DrainCallbacks()              // MPSC: IO 结果 + RPC 响应 + DB 读完成 → 唤醒挂起协程
  DrainTimers()                 // TimerWheel → 触发定时协程 (超时解冻等)
  DrainInternalEvents()         // InternalEventBus 逐 Actor 消耗事件 (≤CascadeBudget/Actor)
  DriveCoroutines()             // 推进所有就绪协程一步 (到 yield 点)
  ProcessActors()               // BeginJob→Execute→Commit/Rollback, 公平调度
  ProjectorSystem()             // 收集脏 BehaviorInfo → 增量推送
  EvictColdBehaviorInfos()      // idle 超阈值的 Actor → 卸载未引用的 BehaviorInfo (4.6)
  PublishCrossServiceEvents()   // CrossServiceEvent → NATS/Redis Streams (6.10)
  TruckCheck()                  // 批量写 MongoDB (脏数据攒批, 定期 flush)
  SleepToNextFrame()
FlushAll()
```

### 7.2 ProcessActors — 公平调度

```
foreach actor in _actors (OrderByDescending StarvationFrames):
  budget = Clamp(baseBudget + starvationFrames/3, min:5, max:25)
  while processed < budget && actor.HasJobs:
    job = actor.DequeueJob()
    try:
      actor.BeginJob()          // 进入 Job 上下文 (SG setter/getter 感知)
      job.Execute()             // 同步完成 或 推进协程到 yield
      if (job.Yielded):          // 协程挂起 (等 RPC/DB), 快照保留跨帧
          actor.ReenqueueJob(job); break
      actor.Commit()            // 清 _bak_*, 清 _dirty_*, dirty → 推送+写库
      job.Complete()            // Reply Ok()
    catch (Exception ex):
      actor.Rollback()          // 遍历 _dirty_*=true, 从 _bak_* 恢复原值 → 清 projectdirtymask
      Log(ex); job.Fail(ex)     // Reply Error()

    if (frameTimeBudgetExceeded): actor.StarvationFrames++; break

  actor.StarvationFrames = actor.HasJobs ? +1 : 0
  StarvationFrames > 60 → Alert + 建议热迁移
```

---

## 八、安全

```
Gateway 入口:
  Rate Limit (Token Bucket, Redis)
  Token 校验 (HMAC session token)
  MaxConnections / MaxPacketSize / ConnectionTimeout
Service 间: 内网 RPC + HMAC 签名 + requestId 去重
```

---

## 九、扩容与容量

### 9.1 单实例容量

```
Actor 壳: ~1.5KB
中等玩家 BehaviorInfo 全量 (Hot): ~1.5MB
  背包(200道具×1KB=200KB) + 邮件(200封×2KB=400KB) + 好友(500×200B=100KB)
  + 任务(~15KB) + 技能/属性(~30KB) + 杂项(~500KB) + GC开销(~200KB)

单实例 (3000 Actor 池):
  2000 Hot:  2000 × 1.5MB = 3,000MB
  500 Warm:  500 × 400KB  = 200MB
  500 Cold:  500 × 1.5KB  = <1MB
  壳+索引:                 ~105MB
  总计:                    ~3.3GB

CPU (20fps, 50ms 帧预算):
  帧开销 ~2.5ms → CPU ~5%; 高峰 ~12ms → CPU ~24%

★ 瓶颈: 内存, 非 CPU
★ 8c16g: 4 实例 × 2000 = ~8,000 CCU
★ 8c32g: 8 实例 × 2000 = ~16,000 CCU
```

### 9.2 100 万在线

| Service | 实例数 | 机器数 | 机型 |
|------|------|------|------|
| player.serv | 500 | ~63 | 8c32g |
| Gateway | 100 | ~13 | 8c16g |
| Router/Controller/其他 | ~10 | 复用 | 4c8g |
| **总计** | **~610** | **~78-82** | |

### 9.3 Actor 热迁移

```
Freeze (停Job, 等协程到 yield ≤1s) → 序列化 BehaviorInfo (Hot~1.5MB; Cold~1.5KB)
→ RPC 传输 → 目标反序列化 → OnEnter → Resume
单 Actor: ~50ms (Hot) / ~1ms (Cold)
并行 20 个/批, 缩容 2000 Actor ≈ 5-10s
```

---

## 十、Controller — 自动扩缩容

Controller 是运维中控进程: Monitor → Decider → Executor → CloudDriver。

```
扩容 (满足任一, 冷却 3min):
  集群 CPU 均值 > 70%, 持续 30s / 实例 ActorCount > 20000 / AvgJobLatencyMs > 10ms

缩容 (全部满足, 冷却 3min):
  集群 CPU < 30%, 持续 5min / ActorCount < 上限50% / 实例数 > minReplicas

每次只扩/缩 1 个实例, 30s 观察期。决策幂等 (decisionId)。
Controller HA: Redis 锁 "controller:leader" (TTL 5s), 脑裂防护。
```

---

## 十一、故障恢复

```
club.service Crash:
  Controller 检测心跳丢失 → 启动新实例
  → MongoDB LoadAll → OnEnter → Router 注册 → 恢复
  恢复时间 ~5 秒, 数据损失 < 200ms
```

---

## 十二、零 GC 策略

```
MessagePack buffer:  ArrayPool<byte>.Shared
临时集合:            ObjectPool 租用, 帧末归还
ProjectorPacket:     ObjectCache 池化 (复用 goblin)
跨进程 RPC:          协程化, 无 TaskCompletionSource
核心路径目标:        GC.Alloc < 1KB/帧
```

---

## 十三、配置

```json
{
  "Gateway": { "Port": 12801, "MaxConnections": 10000 },
  "Router": { "Port": 10010, "Redis": "redis-sentinel:26379" },
  "PlayerService": { "MaxActors": 20000, "ActorDestroySeconds": 300, "FrameBudgetMs": 50 },
  "Persistence": { "MongoDB": "mongodb://mongo:27017/queen" }
}
```

---

## 十四、测试

### 14.1 单元测试

Behavior + DataStore 两类独立可测, 零 Mock。脏位/派生/回滚可验证。

### 14.2 故障注入

```
- RPC 半成功 → 调用方超时重试, 幂等去重
- 进程崩溃 → MongoDB 恢复全部 [Persistent]
- Redis 主从切换 → Router 降级本地缓存
- Gateway 缓存指向已销毁 serv → Redirect 自动重试
- 离线 Actor RPC 激活 → 懒加载 → 正常持久化 → 空闲钝化
- 热迁移中目标 Crash → 源回退, 未迁移继续服务
- 冻结超时 → 自动解冻, 双方还原
- 拍卖退款 RPC 失败 → pendingRefunds 持久化重试
- Job 执行抛异常 → Rollback() + 隔离
```

### 14.3 覆盖率

Queen.Core: 90%+ / Rpc: 80%+ / Network: 80%+ / Persistence: 80%+

---

## 十五、项目结构

```
Queen.sln
├── src/
│   ├── Queen.Core/          # Engine, Comp, Eventor, TimerWheel, CoroutineScheduler
│   │   ├── Containers/      # GBLList, GBLDict, TGBLList, TGBLDict
│   │   ├── Scheduling/      # CoroutineScheduler, WaitForRpc, WaitForLoad
│   │   └── EventBus/        # InternalEventBus, ICoalesceable, CrossServiceEvent
│   ├── Queen.Rpc/           # [RpcService] [RpcMethod] [Persistent] [Projector], SourceGen
│   ├── Queen.Network/       # ITransport, TCP/WS/UDP
│   ├── Queen.Persistence/   # MongoRepository, Truck(BatchWriter), DataStore
│   ├── Queen.Gateway/       # SessionManager, AuthPipeline, RateLimiter
│   ├── Queen.Router/        # ServiceRegistry(Redis), LookupService
│   ├── Queen.Controller/    # Monitor, Decider, Executor, CloudDriver
│   ├── Queen.Server/        # player.serv
│   ├── Queen.Club/Chat/Rank/Auction/Trade/
│   ├── Queen.Ration/        # HTTP 管理 API
│   ├── Queen.Bot/           # 压测
│   └── Queen.DBObserve/     # DB 观测
├── tests/
├── configs/
└── analyzers/Queen.Analyzers/  # QN1001 (禁 async)
```

---

## 十六、设计决策

### 架构决策

| # | 决策 | 理由 |
|------|------|------|
| 1 | Router DNS 模式 | 只寻址不转发; 全直连; 压力极低 |
| 2 | Redis Sentinel HA | 主从+故障转移; 不可用降级本地缓存 |
| 3 | Behavior/BehaviorInfo 分离 | 单例 System + 数据 Component; 统一 Service 骨架 |
| 4 | DataStore 单线程无锁 + 懒加载 | 裸 Dictionary; Get<T> 未命中挂起协程异步读 |
| 5 | [Persistent]/[Projector] 双标志 | 一份结构两标志; 对齐 KBEngine/UE; SG 生成 |
| 6 | Virtual Actor 离线交互 | 在线/离线是业务状态非代码路径; Actor 不在则建壳+Get<T> 懒加载; 单一代码路径 |
| 7 | 下线缓冲期 | 300s 内可重连; resumeToken HMAC 跨 Gateway |
| 8 | Gateway 安全入口 | Rate Limit + Token + 连接限制 |
| 9 | 读写分离 | rank/auction 查询走 Redis 缓存 |
| 10 | 扩缩容 = 部署配置 | 加实例只改 Router hashring; Behavior 代码零改动 |
| 11 | Controller 自动化 + 幂等 | decisionId 防脑裂; TTL 5s |
| 12 | 故障从 MongoDB 恢复 | 不做 WAL; 丢失 < 200ms, 可接受 |
| 13 | Service 间直连 | Router 不碰业务流量 |

### 实现决策

| # | 决策 | 理由 |
|------|------|------|
| 14 | 进程内单线程 + 协程交替 | 绝对无锁; 确定性; 与 goblin 同构 |
| 15 | 禁止 async (QN1001) | 破坏单线程确定性 |
| 16 | IEnumerator 协程跨帧/跨进程 | yield 不阻塞; 单线程交替 |
| 17 | 干掉 Protocols | [RpcService] + SourceGen; MessagePack 统一 |
| 18 | 跨进程 RPC 协程化 + 幂等 | yield 让出线程; at-least-once + requestId 去重 |
| 19 | 冻结-确认替代 2PC | 本地冻结无锁; 幂等确认; 超时解冻 |
| 20 | 公平调度 + 长尾保护 | 饥饿感知 + 动态预算; 帧超时下帧 |
| 21 | TimerWheel O(1) | 替代线性列表 |
| 22 | 持久化中间态 + 幂等重试 | 拍卖退款/交易冻结 [Persistent] 存储; 不做 WAL |
| 23 | IOptions 统一配置 | 环境分层 |
| 24 | Behavior 独立可测 | DataStore + Behavior 零 Mock; 脏位可验证 |
| 25 | 热迁移业务时钟 + 协程可重建 | 迁移期暂停时钟; OnEnter 重算派生 |
| 26 | 零 GC 现实化 | < 1KB/帧; dotnet-counters 实测 |
| 27 | 多核靠多进程 | 单进程单线程; N 实例 = N 核 |
| 28 | Job 级字段快照回滚 | 标量 setter / 容器 Set/Add/RemoveAt 首次写 → `_bak_`+`_dirty_`; Rollback() 反向恢复; Commit() 丢弃; 容器一层深拷贝 |
| 29 | [Projector]/TGBLList 从 goblin 移植 | 前后端同构; CollectDiff 元素级差量 |
| 30 | 派生事件驱动 | OnEnter/RPC/OnLeave; 不 OnTick 全员扫 |
| 31 | 自定义容器 + 一层扁平 | 不暴露 `[]` 索引器, 杜绝元素引用逃逸; Get/Set/Add/RemoveAt; 深嵌套拍平 |
| 32 | InternalEventBus + Coalescing | 级联异步; 同帧同类型事件合并 |
| 33 | 离线 Actor 按需懒加载 | 不做 LoadAll; Get<T> 按类型按需; IO 代价与操作复杂度成正比 |
| 34 | 离线内存保护 | MaxOfflineActors + ActivationRateLimiter |
| 35 | 数据温度 BehaviorInfo 级 | Hot/Warm/Cold; idle 卸载; 不做字段级 |

### 借鉴 Orleans 的关键设计

| 借鉴项 | Queen 落地 |
|------|------|
| Actor 永久可寻址 | Router 离线也返回 serv; `players:{id}` 区分不存在 |
| 激活对调用方透明 | Router.SeekDeep → RPC, 调用方不关心目标是否在内存 |
| 单一代码路径 | Behavior 不区分在线/离线 |
| 空闲钝化 | ActorDestroySeconds (在线离线同一 TTL) |
| 无 Version/锁 | 单进程单线程天然无竞态 |

---

## 十七、实施阶段

| 阶段 | 内容 | 依赖 |
|------|------|------|
| Phase 1 | Queen.Core: Engine, CoroutineScheduler, TimerWheel, MpscQueue, goblin 容器/Projector 移植 + fuzzing | 无 |
| Phase 2 | Queen.Rpc + SourceGen ([RpcService], [Persistent], [Projector], QN1001, JobContext+回滚快照, ProjectorSystem) | Phase 1 |
| Phase 3 | Queen.Network (ITransport, TCP/WS/UDP) | Phase 1 |
| Phase 4 | Queen.Persistence (MongoRepository, Truck, DataStore 懒加载) | Phase 1, 2 |
| Phase 5 | Queen.Router (Redis Sentinel, LookupService, Redirect) | Phase 3 |
| Phase 6 | Queen.Gateway (SessionManager, resumeToken, AuthPipeline) | Phase 3, 5 |
| Phase 7 | Queen.Server (Stage+协程, Behaviors, 推送, 派生, 回滚) | Phase 1-4 |
| Phase 8 | Queen.Club/Chat/Rank/Auction | Phase 7 |
| Phase 9 | Queen.Trade (冻结-确认) | Phase 7 |
| Phase 10 | Queen.Controller (扩缩容) | Phase 5, 7 |
| Phase 11 | Queen.Ration/Bot/DBObserve/Analyzers | Phase 7 |
| Phase 12 | Tests & Polish | 全部 |

**Phase 1 是关键路径地基 — 必须先 fuzzing 覆盖 TGBLList CollectDiff/协程 yield-resume/projectdirtymask 置位。**

---

## 十八、运维与稳定性待办

> 以下 13 项为生产级硬性要求, 需逐项补设计后实施。

### A 组 — 不补会出事故

**18.1 优雅停机**: SIGTERM → drain Job → 迁移/保存 → Truck flush → 退出 (30s 超时)

**18.2 背压**: Job 队列满 → 拒绝 + 上游感知; 推送满 → 断慢客户端; 逐级反压

**18.3 服务间熔断**: 熔断器 (Closed→Open→HalfOpen); 按目标实例粒度; 限流补充

**18.4 重连风暴**: 客户端指数退避+抖动; Gateway Token bucket 分批放行; resumeToken 优先

### B 组 — 不补会卡迭代

**18.5 协议版本兼容**: [RpcService] version; MinClientVersion; 未知字段忽略

**18.6 Schema 演进**: BehaviorInfo SchemaVersion; 懒迁移 (加载时补默认值); 显式脚本

**18.7 灰度发布**: 金丝雀/蓝绿; Router 切流; 新老版本并存 (依赖 18.5)

### C 组 — 不补是黑盒

**18.8 监控告警**: Webhook/IM; 分级 (Info/Warn/Critical); 阈值+持续时间; 静默期

**18.9 结构化日志/追踪**: JSON 格式; requestId 跨进程; Prometheus metrics

**18.10 MongoDB HA**: 3 节点副本集; 全量/增量备份; 恢复演练

**18.11 死信队列**: 补偿 N 次失败 → dead_letters; Ration HTTP API 人工介入

### D 组 — 健壮性边界

**18.12 协程超时**: WaitForRpc timeoutMs 5s; Actor 销毁级联取消; 泄漏检测 60s 告警

**18.13 传输加密**: TLS (TCP/WS); KCP 加密层 (AES-GCM/ChaCha20); 合法 CA

---

## 变更记录

| 版本 | 变更 |
|------|------|
| v0.1-v0.3 | 原始设计; 线程模型迭代; 2PC→冻结-确认; RPC 协程化 |
| v0.4 | [Persistent]/[Projector] 双标志; 删除 OpLog/回滚; 数据安全四件套; 第十七章运维待办 |
| v0.5 | Virtual Actor 离线交互; 删除 [OfflineWritable]/Version; Router 离线穿透 |
| v0.6 | 级联+InternalEventBus+Coalescing; 离线按需懒加载 |
| v0.7 | 数据温度三层模型; 8.1/8.2 容量修正 (瓶颈→内存); 字段级讨论后否决 |
| v0.8 | 干掉 WAL; 三件套; 持久化中间态替代 |
| v0.9 | Job 级字段快照回滚; 四件套 V2 |
| v1.0 | 文档重整理: 合并冗余, 精简章节, 统一术语, Orleans 对比并入决策 |
| v1.1 | Router Seek/SeekDeep 双 API; 自定义容器禁用 `[]` 索引器; 回滚: 容器一层深拷贝, Commit→发布 InternalEvent; Job=原子边界 |
| v1.2 | 跨进程 RPC 拆为 Rpc.Fetch\<T\> (只读快照) + Rpc.Call (目标 Actor 自己执行); 6.9 跨 Actor 写约束对齐 |
