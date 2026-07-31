# Queen 游戏服务端架构

> **版本**：v4.0 · **日期**：2026-07-31
> **状态**：目标架构；当前仓库代码已作废，按本文档从头实现。
> **范围**：核心运行时、数据模型、同步、跨进程交互和必要的进程拓扑。

---

## 1. 定位与目标

### 1.1 Queen 要解决的问题

Queen 不是通用 Actor 框架，也不是微服务脚手架。它面向高并发游戏后端，目标是用一套统一运行时收敛四个矛盾：

1. **并发与正确性**：业务代码不依赖锁，不直接处理 Actor 内数据竞争。
2. **性能与开发体验**：用接近同步的业务代码表达跨帧、数据库和 RPC 流程，同时保持可预算、可观测的调度。
3. **状态与同步**：同一份 `BehaviorInfo` 同时作为业务状态、持久化输入和客户端投影输入，避免重复维护。
4. **单体与分布式**：Actor 在单个 Service 内顺序执行，通过多进程和服务拆分获得水平扩展，而不是把业务对象放进多线程竞争环境。

### 1.2 核心模型

```text
客户端请求
    ↓
Gateway：连接、认证、session、重连
    ↓
Router：定位 Actor 或 Service，不转发业务流量
    ↓
Service：承载 Actor，进程内单线程
    ↓
Actor：状态归属和 Job 串行边界
    ↓
Behavior：业务逻辑
    ↓
BehaviorInfo：业务状态
    ↓
Projector / Truck：客户端投影和 MongoDB 持久化
```

### 1.3 三个必须成立的不变量

1. **唯一归属**：同一个 Actor 在同一时刻只能由一个 Service 实例负责写入。
2. **顺序执行**：同一个 Actor 的 Job 在唯一业务线程上串行执行；不同 Actor 之间协作式交替。
3. **状态可恢复**：已提交状态可以持久化和重建；未提交的本地修改可以回滚；跨边界副作用必须具备幂等或补偿语义。

### 1.4 明确不承诺的能力

以下能力不是第一版运行时的前置条件，不能作为“自动获得”的能力：

- 任意跨 Actor 操作的 ACID 事务
- 跨进程 exactly-once
- 任意时刻透明热迁移
- 挂起 Job 的直接序列化迁移
- 全链路绝对零 GC
- 业务代码无需约束的透明异步加载

它们只能通过明确协议、冻结确认、幂等、补偿和运行时限制逐步实现。

---

## 2. 总体拓扑

```text
                 CLIENT
       TCP / UDP-KCP / WebSocket
                         │
                         ▼
             Gateway（N 实例）
       连接 / 认证 / session / resumeToken
                         │
                         ▼
             Router（HA，DNS 模式）
          只回答“Actor 或 Service 在哪”
                         │
       ┌─────────────────┼─────────────────┐
       ▼                 ▼                 ▼
 player.service N   club.service N    chat/rank/auction/trade
       │                 │                 │
       └────────── Service 间直连 ──────────┘

 Controller（主备）：扩缩容和部署编排
 MongoDB：持久化真相
 Redis：路由、在线状态、缓存和协调数据
```

| 进程 | 职责 | 实例数 |
|---|---|---:|
| `Gateway` | 连接管理、认证、限流、session、重连 | N |
| `Router` | Actor/Service 寻址、路由版本、重定向 | HA |
| `Controller` | 扩缩容、部署编排、实例生命周期 | 主备 |
| `player.service` | 玩家 Actor 的主要归属 | N |
| `club.service` | 公会等共享可变状态 | N |
| `chat.service` | 世界频道和聊天状态 | N |
| `rank.service` | 排行榜等全局视角数据 | 1 或 N |
| `auction.service` | 拍卖 listing 等共享状态 | 1 或 N |
| `trade.service` | 交易冻结、确认和补偿 | 1 或 N |

每个 Service 内部均使用同一骨架：`Engine`、`Stage`、`Actor`、`Behavior`、`BehaviorInfo`、`DataStore`、`Projector` 和 `Truck`。`player.service` 不拥有特殊运行时语义。

---

## 3. 进程内运行时

### 3.1 单线程边界

每个 Service 的业务层只有一个线程：

```text
IO 层：数据库、网络、Redis 允许使用线程池和 async API
  ↓ MPSC callback queue
业务层：唯一线程，裸 Dictionary，Actor/Behavior/Job 全部在此执行
```

规则：

- 业务层禁止 `async/await` 作为 Job 编程模型。
- 业务层禁止同步网络 IO、同步数据库 IO和阻塞等待。
- IO 完成后只能通过 MPSC 队列把结果送回业务线程。
- `DataStore`、Actor 列表和 Behavior 状态不需要锁。
- 多核通过多个 Service 实例实现；单个实例的容量由内存、帧预算和第三方库开销决定。

禁止 `async/await` 的理由不是它必然破坏单线程，而是 Queen 需要让 Job 成为调度器可见的一等对象，并获得精确的主动让出、预算、取消、统计和池化控制。MongoDB 等异步生态通过 `WaitForTask<T>` 适配到 `IEnumerator`，适配层是基础设施的一部分，不泄漏到业务写法。

### 3.2 Job 与协程

业务方法返回 `IEnumerator`，每个 Actor 的 Job 由调度器持有：

```text
Job 创建
  → BeginJob：建立字段快照上下文
  → MoveNext：执行到下一个 yield
  → yield null：下帧继续
  → yield WaitForLoad/WaitForRpc：等待外部结果
  → 完成：Commit
  → 异常/超时/取消：Rollback
```

`IEnumerator` 是可观察的调度对象，但**不承诺跨进程序列化**。Actor 迁移时，挂起 Job 取消并由调用方重试或返回可重试错误；迁移的是已提交 `BehaviorInfo` 快照，而不是运行中的协程状态机。

### 3.3 调度公平性

每帧按 Actor 的 `StarvationFrames` 计算预算：

- 每次 `MoveNext` 到达 `yield` 点，调度器获得一次控制权。
- 每个 Actor 有最小预算和最大预算，避免活跃 Actor 独占线程。
- Job 超过时间或步数阈值记录慢 Job。
- 明确循环必须周期性 `yield return null`；Analyzer 对长循环和同步阻塞 API 报告错误。
- 排序、聚合、批量计算等长同步任务走专用 Service，不放入普通 Actor Job。

协作式调度的硬风险是“业务不 yield 会卡死整个 Service”，这不是运行时可以完全兜底的问题，必须通过 Analyzer、代码审查和运行时慢 Job 监控共同控制。

---

## 4. Virtual Actor

### 4.1 定义

Actor 是数据归属、调度和生命周期单元。玩家、公会、房间、拍卖 listing 都是 Actor，只是组合的 Behavior 不同。

Actor 可以处于：

- **Online**：有客户端 session，状态可推送。
- **Offline active**：无 session，但因好友、邮件、交易等请求暂时激活。
- **Cold**：只有最小身份和路由信息，业务数据不驻留内存。
- **Migrating**：冻结新写入，准备转移所有权。

在线和离线是业务状态，不是两套业务代码。相同的 Behavior 通过不同的投影目标处理有 session 和无 session 的情况。

### 4.2 永久寻址与路由

Redis 中维护：

```text
players:{actorId}       → 永久存在，区分“不存在”和“离线”
online:{actorId}        → {serviceAddr, gatewayAddr}，TTL 5s
services:{type}         → Service 实例集合
services:{type}:hashring → 一致性哈希环
```

Router 提供两个有意不同的 API：

| API | 语义 | 典型场景 |
|---|---|---|
| `Seek(id)` | 只查在线状态，不激活 Actor | 聊天、组队、实时交互 |
| `SeekDeep(id)` | 允许定位归属 Service 并激活离线 Actor | 好友、邮件、交易、公会 |

离线激活是可感知延迟的流程，不伪装成同步 O(1) 查询：寻址、建壳、加载、执行和提交通常跨多个调度周期，调用方必须处理等待、超时和失败。

### 4.3 生命周期与迁移

普通下线流程：

```text
Gateway 断连
  → 删除 online 状态
  → Actor 保留缓冲期（默认 300s）
  → 缓冲期内可被 RPC 激活或重连
  → 超时后 OnLeave、Save、销毁
```

迁移流程必须是排他的状态机：

```text
Prepare → Freeze → Flush → Transfer → Activate → Redirect
```

约束：

- `Freeze` 后旧实例拒绝新写 Job，只允许完成必要的收尾。
- 挂起 Job 取消，客户端或 RPC 调用方根据错误码重试。
- `Flush` 使用版本条件写，确认目标快照已持久化后才激活新实例。
- Router 发布带版本的归属变更；旧路由只能返回 `Redirect`，不能继续写。
- 迁移前刷出已提交的跨服务事件，避免迁移期间重复级联。
- 新旧实例不能同时成为可写主；这是迁移成功的必要条件。

---

## 5. Gateway、Router 与 Service 边界

### 5.1 Gateway

Gateway 负责客户端边界，不直接修改业务数据：

- 连接、认证、限流和协议解码
- 签发 `sessionId` 与跨 Gateway 的 `resumeToken`
- 缓存 `actorId → service` 的短期路由
- 将业务请求转发至目标 Service
- 断线重连时恢复 session 并请求全量投影

`resumeToken` 必须有签名、过期时间和撤销策略。重连时客户端不能仅凭旧连接地址恢复业务所有权。

### 5.2 Router

Router 是 DNS 模式的寻址服务，不转发业务流量：

- Service 注册、心跳和实例摘除
- Actor 在线状态和归属查询
- 一致性哈希定位离线 Actor 的 home Service
- 路由版本和 `Redirect`
- Redis 故障时的短期缓存降级

全直连需要连接池和多路复用，否则 Gateway 与大量 Service 的连接数量会随实例数乘积增长。连接池、重试和故障切换属于 Router/传输层实现，不应由业务 Behavior 自行处理。

### 5.3 Service 拆分原则

满足任一条件时拆出独立 Service：

1. 存在共享可变状态。
2. 需要全局视角。
3. 需要中立协调或冻结确认。
4. 事务模型、容量模型或故障域不同。

典型归属：玩家私有的背包、邮件、好友和任务放在 `player.service`；公会放在 `club.service`；聊天、排行、拍卖和交易分别按全局视角或协调职责拆分。

---

## 6. Behavior 与 BehaviorInfo

### 6.1 Behavior

`Behavior` 是业务逻辑，通常是无状态 System 单例：

- 只通过 `BehaviorInfo` 访问可持久化业务状态。
- 业务方法返回 `IEnumerator`。
- `[RpcMethod]` 标注可调用入口，由 Source Generator 生成协议 stub。
- 派生状态在 `OnEnter`、明确的 RPC 或 `OnLeave` 中计算，不做全员 `OnTick` 扫描。

```csharp
public sealed class WalletBehavior : Behavior<PlayerBehaviorInfo>
{
    [RpcMethod]
    public IEnumerator Spend(ulong actorId, int cost)
    {
        var info = yield return _store.Get<PlayerBehaviorInfo>(actorId);
        if (info.gold < cost)
            yield break;

        info.gold -= cost;
        info.total = info.gold + info.money;
    }
}
```

### 6.2 Job 边界

- Job 是本地原子边界：成功才发布内部事件、回复和投影。
- 一个 Job 只直接写自己的 Actor；跨 Actor 写通过目标 Actor 的 `Call` 执行。
- 禁止无边界的 A → B → A 同步等待循环。
- 通过 `requestId`、调用 hop 和调用栈深度限制检测循环和重复请求。
- 外部副作用不能依赖本地回滚自动撤销，必须延迟到 Commit 后发布，或提供补偿。

### 6.3 BehaviorInfo

`BehaviorInfo` 是纯数据 Component，类体只声明字段，Source Generator 生成属性、序列化、脏标记和快照代码：

```csharp
[Persistent, Projector]
public partial class WalletInfo : BehaviorInfo
{
    public int gold;
    public int money;

    [ProjectorOnly]
    public int total;
}
```

字段语义：

| 声明 | 语义 |
|---|---|
| `[Persistent, Projector]` | 写盘并推送 |
| `[Persistent]` | 只写盘，适合敏感字段 |
| `[Projector]` | 只推送，适合派生或运行时字段 |
| 无声明 | 内部状态 |

实际生成代码可以使用属性或字段包装，但业务必须经过生成的写入口，不能绕过脏标记。

---

## 7. DataStore 与数据温度

### 7.1 DataStore

```csharp
T Get<T>(ulong actorId) where T : BehaviorInfo;
IEnumerator Load<T>(ulong actorId);
IEnumerator LoadAll(ulong actorId);
void MarkSave(ulong actorId);
```

- 命中内存时，`Get<T>` 是 O(1) 查询。
- 未命中时，Job 显式进入等待状态，IO 层异步读取，完成后通过 MPSC 唤醒 Job。
- 同一 Actor/BehaviorInfo 的并发加载必须合并，不能重复打 MongoDB。
- 加载失败、取消、Actor 销毁和迁移都必须唤醒并结束等待 Job。
- 在线登录可使用 `LoadAll`；离线交互按需加载。

`Get<T>` 不应在普通同步代码中隐式阻塞或隐藏不可见的 IO。业务写法可以保持连续，但挂起点必须是调度器可见的 `WaitForLoad`。

### 7.2 数据温度

温度管理以 Actor 为主，只有背包、邮件等大型 BehaviorInfo 进入白名单：

```text
Hot  ：在线活跃，主要状态驻留
Warm ：Actor 保留，白名单大型数据可卸载
Cold ：保留身份和路由信息，业务数据按需加载
```

不做所有 BehaviorInfo 的独立引用计数和精细温度管理。那会显著增加加载状态、引用关系和回收复杂度，收益不足以证明其必要性。

---

## 8. 持久化、脏标记与投影

### 8.1 脏标记语义

脏标记主要表示“需要向外投影的变化”，不承担全局回滚日志职责：

- 标量 setter 置 `projectDirtyMask`。
- 容器的 `Set/Add/RemoveAt` 记录容器差异并置位。
- 帧末收集差异后清理投影脏标记。
- 失败 Job 的回滚同时清理本 Job 产生的投影变化。

### 8.2 Projector

帧末流程：

```text
Actor / BehaviorInfo
  → 检查 projectDirtyMask
  → 收集标量值和容器差异
  → Projection Rules 裁剪/格式化
  → Transport 发送 ProjectorPacket
```

`ProjectorPacket`、容器差异和临时集合使用对象池或 `ArrayPool`。投影协议必须带：

```text
actorId + behaviorInfoType + projectionVersion + baseVersion + payload
```

客户端或 Gateway 发现 `baseVersion` 不连续时请求全量快照，不能继续盲目应用增量。

### 8.3 差异容器

- `GBLList/GBLDict`：持久化容器。
- `TGBLList/TGBLDict`：带投影差异追踪的容器。
- 不暴露会造成引用逃逸的可变元素引用。
- 元素使用 struct 或不可变 class，修改采用替换式。
- 深层嵌套拍平成复合 key，或拆成独立 `BehaviorInfo`。

```text
本帧 CollectDiff：
  added / updated / removed
```

只记录足以重放到客户端的差异；是否需要旧值由投影协议决定，不把容器旧值自动当成全局回滚日志。

### 8.4 持久化

MongoDB 是最终持久化来源，`Truck` 批量写入脏 Actor 的完整持久化字段：

- 使用 `actorId + version` 条件更新。
- 成功写入后递增持久化版本。
- 条件更新冲突不能静默覆盖，必须进入迁移、恢复或人工处理路径。
- 进程崩溃允许丢失最近一个 flush 周期内的已修改数据；第一版不引入 WAL。
- 数据 Schema 必须带版本，读取时支持懒迁移或显式迁移任务。

---

## 9. Job 级回滚与跨边界一致性

### 9.1 本地 Job 回滚

Source Generator 为可写字段生成首次修改快照：

```text
首次写字段：保存旧值，标记 dirty
后续写同字段：只更新当前值
Job 成功：Commit，丢弃旧值
Job 失败：Rollback，恢复旧值并清理脏标记
```

标量字段可以保存旧值；容器保存一层深拷贝。元素必须不可变或替换式更新，避免通过内部引用绕过快照和脏标记。

本地回滚只覆盖当前 Job 尚未 Commit 的内存状态，不覆盖已经发生的外部副作用。

### 9.2 跨 Actor 写入

`Fetch<T>` 是只读快照；`Call` 是目标 Actor 自己执行的写操作：

```text
调用方 → Router 定位目标
      → Call(requestId, operation, args)
      → 目标 Actor BeginJob
      → 校验、修改、Commit/Rollback
      → 返回幂等结果
```

RPC 采用 at-least-once，必须具备：

- 全局或调用域内唯一 `requestId`。
- 目标 Actor 的去重记录和结果缓存。
- 超时、有限重试和退避。
- Redirect hop 上限和调用深度上限。
- 目标不存在、迁移中、过载和版本冲突的明确错误码。

### 9.3 冻结-确认与补偿

跨 Actor 或跨 Service 写操作默认使用异步消息和 Saga；需要多个 Actor 统一提交的关键业务，可以显式使用独立的 `TransactionCoordinator`。协调者是事务状态和提交决议的第三方监工，不持有业务数据，也不要求参与者共享线程或锁。

#### 默认模式：Saga

```text
校验 → 冻结 → 持久化中间态 → 执行各方操作
      → 全部确认 → 提交
      → 超时/失败 → 幂等补偿 → 解冻
```

#### 强事务模式：Prepare/Commit/Confirm

```text
Coordinator: Created
  → Prepare A / Prepare B       // 各 Actor 独立 Job，冻结资源
  → 持久化 CommitDecision       // 只有协调者写入决议后才允许提交
  → Confirm A / Confirm B       // 各 Actor 应用提交并解除冻结
  → Completed

任意 Prepare 失败或取消：
  → 持久化 AbortDecision
  → Cancel A / Cancel B         // 幂等解冻
  → Aborted
```

这里的“原子性”定义为：**所有参与者对外只暴露 `Committed` 或所有参与者最终回到 `Aborted`，中间状态只能表现为 `Pending`/冻结**。不承诺 N 个 Job 在物理上同一时刻执行；协调者故障期间事务可以暂挂，但恢复后只能依据已持久化的单一决议继续 `Confirm` 或 `Cancel`。

强事务模式必须满足：

- 每个事务有全局唯一 `transactionId`，每个参与者操作还带 `participantId`。
- `Prepare`、`Confirm`、`Cancel` 和协调者决议全部幂等；超时只表示结果未知，不能直接重做业务操作。
- 参与者在 `Prepare` 后不得消费或转移被冻结资源；冻结状态属于 `[Persistent]` 数据。
- `CommitDecision`/`AbortDecision` 持久化成功后不可逆，协调者重启后按决议恢复事务。
- 客户端只收到 `Pending` 和最终结果；关键资产在 `Confirm` 完成后才产生最终投影。
- 事务超时、参与者永久离线和重试耗尽进入死信/人工介入，不允许静默解冻或静默提交。

这是一种性能较低但可恢复的可选强一致协议；普通跨 Actor 交互仍然使用 at-least-once 异步消息，不自动升级为全局事务。冻结状态属于 `[Persistent]` 数据，重启后可以恢复。补偿操作必须带原操作 ID，重复执行不能产生额外结果。框架提供 `ICompensatable` 形态和重试模板，业务只实现具体反向操作。

---

## 10. 跨服务事件与级联

同步路径只做当前请求必须完成的最小业务：校验、修改、Commit 和回复。非关键级联通过事件异步推进：

```text
A Commit
  → InternalEvent：同 Actor 内 Behavior 级联
  → CrossServiceEvent：跨服务 at-least-once 消息
  → 目标 Service 在目标 Actor Job 中处理
```

规则：

- 同 Actor 内事件受 `CascadeBudget` 限制。
- 同帧同类型事件可以合并。
- 跨服务事件必须有 eventId、producer version、幂等消费记录和死信路径。
- 热迁移前 flush 已提交但未发送的跨服务事件。
- 事件不能被本地 Job 回滚；失败通过重试或补偿处理。

---

## 11. 主循环

```text
while running:
    DrainCallbacks()          // IO、RPC、DB 结果
    DrainTimers()             // TimerWheel
    DrainInternalEvents()     // Actor 内事件，受预算限制
    DriveCoroutines()         // 就绪 Job 推进到 yield
    ProcessActors()            // 公平预算、Commit/Rollback
    CollectProjection()       // 收集并发送增量
    EvictColdData()           // 按 Actor/白名单降温
    PublishCrossServiceEvents()
    TruckCheck()              // 批量持久化
    SleepToNextFrame()

FlushAll()
```

停机时先停止接收新请求，再等待或取消可取消 Job，flush 持久化数据和跨服务事件，最后注销 Service 路由。

---

## 12. 运行约束与可行性边界

### 12.1 第一版必须验证

1. 同一 Actor 的 Job 永不并行。
2. 协程在 `yield` 后能正确恢复，异常和取消不会泄漏 Job。
3. 同一字段在跨帧 Job 中的快照语义正确。
4. `GBL/TGBL` 的增量收集在随机 Add/Update/Remove 序列下可重放。
5. MongoDB 条件写不会被旧版本覆盖。
6. 投影版本断裂可以回到全量快照。
7. RPC 重试不会重复执行不可重复操作。

### 12.2 性能目标的表达

GC 目标分级，而不是宣称绝对零分配：

- Queen 自有热路径：目标小于 `1KB/帧`，以基准测试验证。
- 整进程：目标小于 `5KB/帧`，包含第三方库，仅作为工程目标。
- 真正 SLA：长时间压测下 Gen2 间隔、暂停时间、P99 延迟和吞吐量。

MongoDB 驱动、网络库和序列化库的分配不完全由 Queen 控制，不能把第三方库行为计入“架构保证”。

### 12.3 工程可行性分级

| 能力 | 判断 | 说明 |
|---|---|---|
| 单线程 Service、Actor、Job | 高 | 运行时边界清晰，可先单进程验证 |
| Behavior/BehaviorInfo、SourceGen | 高 | Roslyn Generator 可实现，测试量较大 |
| 脏标记、差异投影、MongoDB 条件写 | 高 | 协议版本和容器语义必须先定 |
| Job 级本地回滚 | 高 | 只覆盖未提交的本地状态 |
| 单机跨 Service RPC | 中高 | 先验证幂等、超时和 Redirect |
| Router、Gateway、在线离线 | 中高 | 依赖故障和缓存失效语义 |
| Actor 迁移 | 中 | 需要排他状态机和版本化所有权 |
| 跨 Service 交易 | 中 | 依赖冻结、确认、补偿，不能靠回滚自动解决 |
| 零 GC 与超高 CCU | 未定 | 必须由真实业务基准测试决定 |

---

## 13. 分阶段落地路线

### Phase 1：单进程核心运行时

`Engine`、单线程调度、Actor 生命周期、Job、协程等待、超时、取消、异常隔离、公平调度。

**退出条件**：Actor 串行性、yield-resume、慢 Job 和异常隔离测试通过。

### Phase 2：BehaviorInfo 与数据层

`Behavior/BehaviorInfo`、DataStore、Persistent SourceGen、MongoDB 版本写、Job 快照回滚、温度管理。

**退出条件**：随机修改、失败恢复、重启加载和条件写冲突测试通过。

### Phase 3：Projector 与客户端同步

Projector SourceGen、TGBL 容器、CollectDiff、快照/增量协议、版本断裂恢复、对象池。

**退出条件**：客户端可通过任意增量序列重建正确状态，断线可全量恢复。

### Phase 4：单机多 Service RPC

`Fetch`、`Call`、requestId 去重、超时、重试、Redirect、事件和死信。

**退出条件**：服务重启、重复包、超时重试和迁移中错误码测试通过。

### Phase 5：Gateway、Router 与 Actor 迁移

寻址、在线状态、session/resumeToken、路由版本、Freeze/Flush/Transfer/Activate、连接池和多路复用。

**退出条件**：重连、路由缓存失效、单主迁移和迁移失败恢复测试通过。

### Phase 6：分布式一致性与运维

冻结确认、补偿框架、Schema 迁移、协议兼容、灰度、监控、链路追踪、备份恢复和死信人工介入。

**原则**：Phase 1-3 证明运行时和数据模型；Phase 4-6 才扩展分布式边界，不同时实现全部目标。

---

## 14. 设计决策摘要

| # | 决策 |
|---:|---|
| 1 | 进程内单线程，协程交替；多核靠多进程 |
| 2 | 业务层禁 `async/await`，IO 通过适配层接入 |
| 3 | Actor 是唯一状态归属和 Job 调度边界 |
| 4 | Online、Offline active、Cold、Migrating 是显式生命周期状态 |
| 5 | Router 只寻址不转发，Gateway 只做客户端边界 |
| 6 | 所有 Service 共用统一运行时骨架 |
| 7 | `Behavior` 管逻辑，`BehaviorInfo` 管纯数据 |
| 8 | `[Persistent]` 和 `[Projector]` 由 Source Generator 生成实现 |
| 9 | DataStore 懒加载必须显式挂起，不能隐藏阻塞 IO |
| 10 | 脏标记服务于投影；Job 级快照服务于本地失败回滚 |
| 11 | 容器采用替换式元素和一层扁平结构 |
| 12 | MongoDB 是持久化真相，使用版本条件写，第一版不做 WAL |
| 13 | `Fetch` 读快照，`Call` 由目标 Actor 自己执行写入 |
| 14 | RPC 使用 at-least-once、requestId 去重和有限重试 |
| 15 | 跨边界默认使用异步 Saga；关键业务可选 `TransactionCoordinator` 的 Prepare/Confirm/Cancel 强事务 |
| 16 | 投影带版本，断裂时回退全量快照 |
| 17 | 级联使用本地事件和跨服务事件，跨服务事件必须幂等 |
| 18 | Actor 迁移取消挂起 Job，迁移已提交快照，不迁移协程状态机 |
| 19 | 协作式风险由 Analyzer、预算和慢 Job 监控共同约束 |
| 20 | 性能目标必须由真实压测验证，不把零 GC 当作架构保证 |
