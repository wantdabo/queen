# Queen 游戏服务端架构

> **版本**：v4.1 · **日期**：2026-08-02
> **状态**：设计定稿；当前仓库代码已作废，按本文档从头实现。
> **范围**：核心运行时、数据模型、同步、跨进程交互和进程拓扑。

---

## 1. 定位与目标

### 1.1 定位

Queen 面向高并发游戏后端，用一套统一运行时收敛四个矛盾：

1. **并发与正确性**：业务代码不依赖锁，不直接处理 Actor 内数据竞争。
2. **性能与开发体验**：用接近同步的业务代码表达跨等待点（yield/IO/RPC）的流程，同时保持可预算、可观测的调度。
3. **状态与同步**：同一份 `BehaviorInfo` 同时作为业务状态、持久化输入和客户端投影输入，避免重复维护。
4. **单体与分布式**：Actor 在单个 Service 内顺序执行，通过多进程和服务拆分获得水平扩展，而不是把业务对象放进多线程竞争环境。

### 1.2 核心模型

```text
客户端请求
    ↓
Gateway：连接、认证、session、重连
    ↓
Compass：定位 Actor 或 Service，不转发业务流量
    ↓
Service：承载虚拟 Actor 注册表（ID→DataStore）与行为生命周期，进程内单线程
    ↓
Actor：纯 ID（ulong），无 class，串起 BehaviorInfo 的注册键
    ↓
Behavior：业务逻辑（纯逻辑，注入 actorId + engine，四钩子）
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

以下能力不是第一版运行时的前置条件：

- 任意跨 Actor 操作的 ACID 事务
- 跨进程 exactly-once
- 任意时刻透明热迁移
- 挂起 Job 的直接序列化迁移
- 全链路绝对零 GC
- 业务代码无需约束的透明异步加载
- 客户端已确认交易在崩溃窗口内的持久化（收到投影 ≠ 持久化成功，见 8.4）

它们通过明确协议、冻结确认、幂等、补偿和运行时限制逐步实现。

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
             Compass（HA，DNS 模式）
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
| `Compass` | Actor/Service 寻址、路由版本、重定向 | HA |
| `Controller` | 扩缩容、部署编排、实例生命周期 | 主备 |
| `player.service` | 玩家 Actor 的主要归属 | N |
| `club.service` | 公会等共享可变状态 | N |
| `chat.service` | 世界频道和聊天状态 | N |
| `rank.service` | 排行榜等全局视角数据 | 1 或 N |
| `auction.service` | 拍卖 listing 等共享状态 | 1 或 N |
| `trade.service` | 交易冻结、确认和补偿 | 1 或 N |

每个 Service 内部均使用同一骨架：`Engine`（怎么跑）、`Service`（容器，谁是谁）、`JobScheduler`（调度）、`Behavior`、`BehaviorInfo`、`DataStore`、`Projector` 和 `Truck`。Actor 不是类型——是 Service 容器内的 `ulong` ID 键，无独立 class（见 4.1）。`player.service` 不拥有特殊运行时语义。

---

## 3. 进程内运行时

### 3.1 单线程边界

每个 Service 的业务层只有一个线程：

```text
IO 层：数据库、网络、Redis 允许使用线程池和 async API
  ↓ MPSC callback queue
业务层：唯一线程，裸 Dictionary，Actor(ID)/Behavior/Job 全部在此执行
```

规则：

- 业务层禁止 `async/await` 作为 Job 编程模型。
- 业务层禁止同步网络 IO、同步数据库 IO和阻塞等待。
- IO 完成后只能通过 MPSC 队列把结果送回业务线程。
- `DataStore` 注册表、Behavior 状态不需要锁。
- 多核通过多个 Service 实例实现；单个实例的容量由内存、调度吞吐和第三方库开销决定。
- 每个 Actor 同一时刻只执行一个 Job：调度切换只发生在不同 Actor 的 Job 之间，同一 Actor 的 Job 严格串行。此语义适用于所有 Service（player、club 等），与 Service 承载的 Actor 类型无关。

`async/await` 不作为 Job 编程模型：Job 必须是调度器可见的一等对象，以获得精确的主动让出、预算、取消、统计和池化控制。异步生态通过 `WaitForTask<T>` 适配到 `IEnumerator`，适配层属于基础设施，不泄漏到业务写法。

### 3.2 Job 与协程

业务方法返回 `IEnumerator`，每个 Actor 的 Job 由调度器持有：

```text
Job 创建
  → BeginJob：建立字段快照上下文
  → MoveNext：执行到下一个 yield
  → yield null：下一轮调度继续
  → yield WaitForLoad/WaitForRpc：等待外部结果
  → 完成：Commit
  → 异常/超时/取消：Rollback
```

`IEnumerator` 是可观察的调度对象，**不承诺跨进程序列化**。Actor 迁移时，挂起 Job 取消并由调用方重试或返回可重试错误；迁移的是已提交 `BehaviorInfo` 快照，而不是运行中的协程状态机。

Job 持有引用的 `BehaviorInfo` 受引用计数保护：Job 通过 `DataStore.Get` 获得引用时对该实例计数加一，Job 结束（Commit/Rollback/异常/取消）时统一释放；计数大于零的 `BehaviorInfo` 在任何卸载判定中被跳过。挂起中的 Job 跨调度轮次持有的引用因此不会因冷卸载悬空。卸载只发生在主循环的 `EvictColdData` 阶段，此时所有 Job 均处于 yield 挂起点，引用计数生命周期与 Job 生命周期一一对应，无需全局引用追踪。

**等待超时与取消（R10 定案）**：每个等待器（`WaitForLoad`/`WaitForRpc`）必须带 deadline（框架给默认值、调用方可覆盖），超时视为一次唤醒，与取消同路径：唤醒等待 → Job 失败回滚。挂起不阻塞 Service——等待期间调度器让出控制权、继续执行其他 Actor 的 Job（player.service 有 N 个玩家，一个玩家 Job 挂起不影响他人）。**取消链仅由生命周期事件触发**：Actor 销毁/迁移 → 取消其 Job → 逐一唤醒挂起的等待器；**玩家下线不触发取消**——下线与业务流程无关，Actor 壳常驻、Job 照常推进，离线所需的 BehaviorInfo 走 7.1/10.1 离线读写按需加载路径。Job 总时长由各等待段的 deadline 界定，不设独立全局计时器；过慢 Job 由 3.3 慢 Job 监控告警。

### 3.3 调度公平性与卡死防护

调度器维护就绪集合（ready set）：只有持有可推进 Job 的 Actor 才进入调度循环，避免每轮遍历全部 Actor 的 O(N) 全扫（R4 定案，调度器第一版即实现）。

每轮调度循环按 Actor 的 `starvationFrames` 计算预算：

- 每次 `MoveNext` 到达 `yield` 点，调度器获得一次控制权。
- 每个 Actor 有最小预算和最大预算，避免活跃 Actor 独占线程。
- Job 超过时间或步数阈值记录慢 Job。
- 显式循环必须周期性 `yield return null`；Analyzer 对长循环和同步阻塞 API 报告错误。
- 排序、聚合、批量计算等长同步任务走专用 Service，不放入普通 Actor Job。

卡死防护由三层构成：

1. **Analyzer 静态检查**：长循环、同步阻塞 API 编译期报错。
2. **墙钟预算 + 慢 Job 取消（R21 追加）**：调度器为每轮 `MoveNext` 段打点计时，段返回后结算；超预算（阈值可配）→ 记录慢 Job + 告警，并标记该 Job，在**下一个 yield 点协作式取消 + 回滚**（R10 取消链 + `_bak_` 快照回滚，结束语义 = 取消 + 回滚，可预测）；对无 yield 的单段卡死无效（预算检查执行不到），该场景由第 3 层 + 进程级兜底。
3. **运行时看门狗**：调度线程每轮推进刷新心跳；独立监控线程发现心跳超时（>500ms 未推进）即判定卡死，dump 当前协程调用栈到日志。

看门狗是线上卡死的第一定位手段，不依赖业务侧配合。

**CPU 密集卡死边界（R21 定案）**：超时检测分两层——①**等待段超时保证**：等待器（`WaitForLoad`/`WaitForRpc`）强制 deadline，超时 = 唤醒 + Job 失败回滚（3.2），Job yield 后调度器有控制权、检查有机会执行，此层可保证；②**无 yield 的 CPU 密集段不会超时**：检查只在 yield 点/等待器触发，卡死段调度器无控制权、看门狗只能 dump。**进程内强制中断 + 回滚不可实现**：协程协作式、无 yield 点即无抢占注入点；强杀线程时回滚逻辑（`_bak_` 快照恢复）在 Job 栈上执行不到、内存半更新状态更糟，单线程模型没有第二个执行上下文能跑回滚。**因此卡死兜底在进程级**：不可控/CPU 密集工作下沉专用 Service 进程（本条即"长同步任务走专用 Service"规范约束的运行时依据），外部看门狗（Controller）按心跳超时杀进程重启，回滚 = 崩溃窗口语义（8.4：内存 dirty 丢弃、Mongo 真相恢复、<200ms 丢失可接受），故障半径圈死、不伤主 `player.service`。

**墙钟预算边界（R21 追加，2026-08-02）**：预算检查在 `MoveNext` 段返回后 / yield 点执行——能抓住"慢但会返回"的段（记慢 Job + 告警 + 下一个 yield 点取消回滚），**抓不住"永不返回"的无 yield 卡死段**（段未返回则检查点执行不到）。卡死段仍由看门狗 dump + 专用 Service 进程级杀重启兜底；`WaitForTask<T>`（3.2）为可拆纯计算段提供"抛线程池 + deadline 回滚"的进程内隔离路径（子任务不碰共享状态，卡死仅泄漏有界线程、不阻塞主线程）。

### 3.4 背压：队列有界与满策略（R13 定案）

**问题定性**：背压是**资源有界性/可用性问题**，不是性能问题——性能问题是"慢"，背压问题是"死"（OOM/雪崩）。单线程共享下，一个 Actor 的队列被塞爆吃掉的是整个进程的内存，最终所有玩家一起挂。

**有界对象**：三类队列全部必须有上限——IO callback MPSC（3.1）、Actor Job 队列（3.2）、加载唤醒队列（7.1）。无界队列 = 风暴无限吞内存。

**背压由三道限额构成（调度器骨架在 Phase 1 建立时即落地，Phase 2 完善）**：

1. **Job 限额（Actor 级）**：每个 Actor 的 Job 队列上限 `JOB_QUEUE_CAP`（默认值可配置）。满 → 新请求拒绝：返回 busy 错误码，客户端退避重试。单 Actor 刷屏风暴被关在自己队列里，不穿透其他 Actor（隔离性）。
2. **must 限额（Service 级）**：must 走独立队列 + `MUST_BUDGET_PER_FRAME` 配额（9.2），与普通 Job 互不挤占；must 队列本身有长度上限，满 → 拒绝新 must + 告警。恶意高频 must 只能占 must 配额，不能饿死任何普通 Job。
3. **PPS 限制（Service 级）**：每 Service 每 Actor 请求速率上限（令牌桶，默认值可配置），超限拒绝 + 告警。Job 队列有界只挡"堆积"、挡不住"高速重投"——刷屏的最终防线在此；与 Gateway 入口限流（5.1）双保险：Gateway 挡外部毛流量，Service 内 PPS 挡内部恶意/异常流量。

**满策略三选一（按队列性质选择，默认拒绝+告警）**：

- **拒绝+告警**（默认）：业务队列（Job/must），返回错误码，客户端退避重试。
- **丢弃**：仅限可丢场景（通知、纯表现消息），丢了由全量/刷新兜底（8.2 投影整包恢复）。
- **降级**（可选）：优先级队列，低优先级先丢/先拒绝。

看门狗（防卡死）与背压（防打爆）是两回事：前者保证调度线程活着，后者保证队列和速率有界。

以上为**入站背压**；出站方向（Gateway → 客户端发送缓冲）的对称约束见 8.2"出站背压（慢客户端）"。

---

## 4. Virtual Actor

### 4.1 定义

**Actor 收窄为纯身份：一个 `ulong` ID，没有 class。** 玩家、公会、房间、拍卖 listing 都是 actorId，只是挂载的 `BehaviorInfo`/`Behavior` 不同。"把 BehaviorInfo 串起来"的机制是 **Service 容器的 ID→DataStore 注册表**（`Service.stores`）：

- `Service.AddActor(id)`：登记虚拟 Actor，返回其 `DataStore`（重复 ID 抛异常）——数据注册表入口；同时自动装配默认行为集（Service 创建事件反射扫描到的全部 `Behavior` 子类，见 6.1/11），业务零注册。
- `store.AddInfo<T>()`：显式把某个 `BehaviorInfo` 挂到该 Actor 名下（`new T()` + 注册），数据全部挂在 DataStore 上。
- `Service.Active/Deact(id)`：驱动该 Actor 所有 Behavior 的活跃生命周期（两件套）；`Service.RemoveActor(id)`：对称收尾（在线先 Deact → 销毁 store）。**无 `Load/Unload` API**——数据进出内存是框架内部事务（7.1/7.2），业务无感。
- 调度边界：`JobScheduler.Post` 经 `engine.service.GetStore(actorId)` 把 Job 直连到该 Actor 的 DataStore，Job 内 `JobContext.Get<T>()` 恒取本 Actor 数据——未命中时框架自动挂起加载，开发者无感。

Actor 生命周期由两个正交维度构成：

- **活跃（Active/Deact）**：是否有可推送投影目标。player 的活跃 = session 建立（原 Online）；club 等无 session 类型的活跃 = 有在线成员/被激活。进入活跃触发 `OnActive`，离开活跃触发 `OnDeact`。
- **激活（框架内部概念，无业务钩子）**：业务数据是否在内存（Hot）。虚拟化的本质承诺 = **开发者眼里 actor 存在 = 数据存在**，"拉到存在"是框架义务（7.1 懒加载 / 7.2 冷卸载的内部事务），**没有 `OnLoad`/`OnUnload` 钩子**——数据在不在内存开发者无感，任何 `Get<T>()` 拿到就是拿到了，中间是否触发加载、是否挂起无感；被 `SeekDeep` 拉起的离线 Actor 只有激活没有活跃。

派生状态：

- **活跃**：有可推送投影目标（player：session 建立；club：有在线成员），状态可推送。
- **非活跃已激活**：数据在内存但无投影目标（原 Offline active：player 无 session 被好友/邮件/交易拉起；club 被拉起后无成员在线）。
- **Cold**：只有最小身份和路由信息（ID 已登记），业务数据不驻留内存。
- **Migrating**：冻结新写入，准备转移所有权。

**Actor 虚拟化（离线激活/迁移）以此为前提**：离线 = ID 存在但数据不在内存，`SeekDeep` 激活 = Service 建壳（登记）+ 按需加载 DataStore；迁移移动的是已提交 `BehaviorInfo` 快照，ID 本身无状态。活跃与非活跃是业务状态，不是两套业务代码。相同的 Behavior 通过不同的投影目标处理有投影目标和无投影目标的情况。

### 4.2 永久寻址与路由

Redis 中维护：

```text
players:{actorId}       → 永久存在，区分“不存在”和“离线”
online:{actorId}        → {serviceAddr, gatewayAddr}，TTL 5s
services:{type}         → Service 实例集合
services:{type}:hashring → 一致性哈希环
```

Compass 提供两个 API：

| API | 语义 | 典型场景 |
|---|---|---|
| `Seek(id)` | 只查在线状态，不激活 Actor | 聊天、组队、实时交互 |
| `SeekDeep(id)` | 允许定位归属 Service 并激活离线 Actor | 好友、邮件、交易、公会 |

离线激活是跨多个调度周期的可感知延迟流程（寻址 → 建壳 → 加载 → 执行 → 提交），调用方必须处理等待、超时和失败。

**离线激活即排他归属获取（R16 否决定案）**：`SeekDeep` 的 home 由一致性哈希**确定性**定位——同一 actorId 在同一哈希环视图下恒指向唯一 home Service，离线激活只在该 home 上发生一次。多个调用方并发 `SeekDeep` 同一 actorId（A 的好友邮件、B 的交易请求）按同一哈希**收敛到同一 home**，由 home 单线程调度 + 激活幂等（已激活复用 / 激活中等待）消化并发，**无需额外激活锁**。唯一可能破坏确定性的因素是哈希环视图不一致（节点增减）——该窗口由 4.3 迁移状态机覆盖（版本条件写 + Compass 版本化归属 + `Redirect`，新旧实例不可同时可写），与离线激活共用同一条排他归属防线，不新增机制。

**在线状态续期（R6 定案）**：`online:{actorId}` 由 **Actor 宿主的 Service** 续期——只要 Actor 处于内存活跃态（Hot 或下线缓冲期 4.3）就保持续期，频率 ≤ TTL/2（默认 2s，可配），由 Service 的 Actor 管理循环**批量续期**（合并 Redis 往返）。看门狗心跳（3.1）是进程级 liveness，与 Actor 级 online 续期解耦：Service 崩溃 → online 自然过期 → Compass 停止向该实例寻址，重启或迁移后重新注册。**Gateway 不续期 online**：连接与 Actor 活跃解耦，缓冲期 300s 内 Actor 仍在内存、路由必须保持。

### 4.3 生命周期与迁移

离开活跃流程（player 视角，club 等无 session 类型的活跃由成员在线驱动、无此 Gateway 流程）：

```text
Gateway 断连（投影目标消失）
  → 删除 online 路由状态
  → Actor 保留缓冲期（默认 300s）
  → 缓冲期内可被 RPC 激活或重连
  → 超时后 OnDeact（离开活跃）、Save、销毁
```

迁移是排他状态机：

```text
Prepare → Freeze → Flush → Transfer → Activate → Redirect
```

约束：

- `Freeze` 后旧实例拒绝新写 Job，只允许完成必要的收尾。
- 挂起 Job 取消，客户端或 RPC 调用方根据错误码重试。
- `Flush` 使用版本条件写，确认目标快照已持久化后才激活新实例。
- Compass 发布带版本的归属变更；旧路由只能返回 `Redirect`，不能继续写。
- 迁移前刷出已提交的跨服务事件，避免迁移期间重复级联。
- 新旧实例不能同时成为可写主；这是迁移成功的必要条件。
- 迁移期间保活续期（R6 定案）：进入 `Prepare` 时对 `online:{actorId}` 做**显式保活**（TTL 覆盖整个迁移窗口，如 30s），归属变更发布后由新实例接管常规续期；旧实例在 `Redirect` 前不撤销该记录。

---

## 5. Gateway、Compass 与 Service 边界

### 5.1 Gateway

Gateway 负责客户端边界，不直接修改业务数据：

- 连接、认证、限流和协议解码
- 签发 `sessionId` 与跨 Gateway 的 `resumeToken`
- 缓存 `actorId → service` 的短期路由
- 将业务请求转发至目标 Service
- 断线重连时恢复 session 并请求全量投影

`resumeToken` 必须有签名、过期时间和撤销策略。重连时客户端不能仅凭旧连接地址恢复业务所有权。

**顶号与会话并发**：`sessionId`/`resumeToken` 只覆盖断线重连，还需覆盖**同账号多端登录**。默认**单会话**：同一 `actorId` 任一时刻只允许一个有效 session。

- session 带**版本号（epoch）**：认证成功即递增该账号的 session epoch，旧 session 与其 `resumeToken` 同步失效（`resumeToken` 签名含 epoch）。
- **顶号时序**：新连接认证成功 → 递增 epoch → 更新 `online:{actorId}.gatewayAddr` 指向新 Gateway → 通知旧 Gateway 撤销旧 session（关连接、清空出站缓冲）→ 旧客户端后续请求全部失败，只能重新登录。
- **归属切换的投影一致性**：投影寻址复用 `online:{actorId}.gatewayAddr`（8.2），顶号后该映射已指向新 Gateway，Service 的自然投递即转向新连接；旧 Gateway 在撤销完成前发出的残留投影随 session 撤销丢弃，重连/重登走全量快照兜底（8.2 commitId 校验）。
- **与断线重连的区分**：`resumeToken` 只能恢复"仍是当前有效"的 session（epoch 未变）；被顶号后 epoch 已变，旧端重连视为新认证或直接拒绝——不会出现"旧端以为自己在线、投影还投给旧端"的幻象。

**安全边界（R11 定案）**：**公网边界 = Gateway 客户端侧，且只有 KCP 走公网**；Gateway 与服务端（Compass/Service）之间、Service 之间全部内网明文，不加密。因此加密只做一件事：**Gateway 公网侧 KCP 数据面加密**（KCP 是可靠传输层，加密加在协议层——应用层包级对称加密，会话密钥随认证/`resumeToken` 流程协商；具体方案属 Phase 3 网络层实现细节，这里只承诺"公网边界加密必须存在"）。内网链路不引入任何加密开销。

### 5.2 Compass

Compass 是 DNS 模式的寻址服务，不转发业务流量：

- Service 注册、心跳和实例摘除
- Actor 在线状态和归属查询
- 一致性哈希定位离线 Actor 的 home Service
- 路由版本和 `Redirect`
- Redis 故障降级（见下）

**Redis 故障降级（R6 定案）**：Compass **读路径全走内存镜像**（`online`/`players`/hashring 常驻镜像，Redis 只做真相源）。Redis 故障时按可配置**陈旧窗口**（默认 60s）继续服务读；写（注册/续期/归属变更）失败进入**待同步队列**，Redis 恢复后回放。超过陈旧窗口仍未恢复 → 新寻址/注册拒绝，或回退一致性哈希 fallback（离线 Actor 定位 home Service 不依赖 `online`）。**Redis 故障只影响"新变更可见"，不影响既有寻址**。

连接池、多路复用、重试和故障切换属于 Compass/传输层实现（全直连下 Gateway 与 Service 的连接数随实例数乘积增长），业务 Behavior 不自行处理。

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

`Behavior` 是业务逻辑，**纯逻辑、不强绑定 BehaviorInfo**：不继承组件树（无 `Comp`），注入面只有 `actorId` + **受限门面**（日志/时钟，不含 Service、不含容器；waitable/timer 均不入面——等待器是 `yield return` API 的一部分，TimerWheel 是 Service 级系统家政设施见 3.4），通过两个活跃生命周期钩子接入调度；**装配由框架自动完成**——Service 创建事件反射扫描全部 `Behavior` 子类，`AddActor` 自动实例化挂载默认行为集，业务零注册（见 11）；`Behavior<T> where T : BehaviorInfo` 泛型基类作废，Job 内取数据一律 `JobContext.Get<T>()`（IWaitable，命中即续、未命中自动挂起加载）：

- 只通过 `BehaviorInfo` 访问可持久化业务状态。
- 业务方法返回 `IEnumerator`。
- `[RpcMethod]` 标注可调用入口，由 Source Generator 生成协议 stub。
- `[RpcMethod]` 的执行主体（actorId）**由框架从 session/认证上下文注入，调用者恒为自己**，签名不允许客户端指定目标（R5 定案，见 9.3）；需要作用于其他玩家时，目标作为**业务参数**传入，由业务代码做关系/权限校验。
- 派生状态在 `OnActive`、明确的 RPC 或 `OnDeact` 中计算，不做全员 `OnTick` 扫描。

**生命周期两件套（2026-08-04 修正：撤销 `OnLoad`/`OnUnload`，由 Service 驱动）**：行为生命周期只保留**活跃维度**两个钩子，不引入 `OnTick` 扫描。**数据进/出内存（激活维度）不是业务钩子**——虚拟化后"数据在不在内存"是框架数据层（7.1 懒加载 / 7.2 冷卸载）的内部事务，业务无感。钩子挂在 Behavior 上，由 Service 统一驱动：`Service.Active(id)` 触发该 Actor 全部 Behavior 的 `OnActive`，`Deact` 同理，`RemoveActor` 在线先 Deact 再销毁 store：

- `OnActive` 进入活跃：Actor 进入活跃态（出现可推送投影目标：player = session 建立；club = 有在线成员）时触发，登录 Job 挂载点。
- `OnDeact` 离开活跃：Actor 离开活跃态（投影目标消失：player = 缓冲期 4.3 结束；club = 最后成员离线）时触发，收尾/注销。

派生状态在 `OnActive`、明确的 RPC 或 `OnDeact` 中计算；周期性派生刷新走 Actor 级周期任务（11），不在此列。

```csharp
public sealed class WalletBehavior : Behavior
{
    [RpcMethod]
    public IEnumerator Spend(int cost)
    {
        // 无参：恒取本 Actor 的 Info（IWaitable：命中即续、未命中自动挂起加载）
        var info = yield return JobContext.Get<PlayerBehaviorInfo>();
        if (info.gold < cost) yield break;

        info.gold -= cost;
        info.total = info.gold + info.money;
    }

    [RpcMethod]
    public IEnumerator SendGift(ulong targetActorId, int itemId, int count)
    {
        // 目标玩家是业务参数：服务端业务代码必须做关系/权限校验，框架不拦截
        if (false == Relation.Exists(CallContext.actorId, targetActorId)) yield break;
        // 校验通过后执行，主体仍是 CallContext.actorId 自己
    }
}
```

跨 Actor 访问走 **Radio 壳**（Source Generator 生成的强类型 ActorRef，关键字 `radio`）。壳树分**两棵独立子树**——数据层与逻辑层分开，**不假设 Behavior 与 Info 成对**：

- `radio.info.*`：**Info 壳**（数据层，多例）——只暴露 `[Fetchable]` 标记字段，生成只读快照属性；
- `radio.behavior.*`：**Behavior 壳**（逻辑层，单例）——只暴露 `[Remote]` 标记方法（内部 RPC，= Call）；客户端 `[RpcMethod]` 不进壳。

**取壳零成本、子壳按需拉取**：`Get<R>(actorId)` 是 O(1) 纯壳引用（不拉数据、不挂起）；`radio.info.bag` 是**子壳获取器**（返回 `IEnumerator`），`yield return` 挂起直到该子树 `[Fetchable]` 快照就绪——真正"用才拉"。粒度是子壳不是字段（属性 getter 不能 `yield` 的物理约束）；`radio.behavior.*` 的 `[Remote]` 方法本身就是 RPC，天然按需。

```csharp
// 数据层：Info 字段标记 [Fetchable] → 进 radio.info.*
[Persistent]
public partial class BagInfo : BehaviorInfo
{
    [Fetchable] public partial int capacity { get; set; }
    [Fetchable] public partial int count { get; set; }
}

// 逻辑层：Behavior 方法标记 [Remote] → 进 radio.behavior.*（无需对应 Info）
public sealed class BagBehavior : Behavior
{
    // 客户端可调 → 不进壳
    [RpcMethod] public IEnumerator Sell(ulong itemId);
    // 内部 RPC → radio.behavior.bag.Give
    [Remote] public IEnumerator Give(ulong toId, int itemId, int count);
}

// 我方业务代码：
[RpcMethod]
public IEnumerator AskGift(ulong friendId, int itemId, int count)
{
    // O(1)：纯壳引用，零拉取、零挂起
    var radio = Get<Radio>(friendId);
    // 用才拉：只拉 BagInfo 的 [Fetchable] 子集（子壳获取器）
    var bag = yield return radio.info.bag;
    // 拿到子壳后字段同步读缓存（弱一致）
    if (0 == bag.count) yield break;
    // RPC 天然按需，friend 自己执行
    yield return radio.behavior.bag.Give(CallContext.actorId, itemId, count);
}
```

- `radio.info` 与 `radio.behavior` 按名字独立生成：Behavior 与 Info **不强制成对**，只有对应声明的一方才生成子树。
- 子壳**幂等缓存**：同一 actor 同一子壳只拉一次，重复 `yield return radio.info.bag` 立即返回缓存、不再产生 IO/RPC。
- 壳字段是**弱一致快照**（不保证实时性），强一致读/写走 `radio.behavior.*` 的 `[Remote]` 方法（见 9.3）。

### 6.2 Job 边界

- Job 是本地原子边界：成功才发布内部事件、回复和投影。
- 一个 Job 只直接写自己的 Actor；跨 Actor 写通过目标 Actor 的 `Call` 执行。
- 同 Service 多 Actor 的原子批操作由 `must` 原语显式化（见 9.2）。
- 禁止无边界的 A → B → A 同步等待循环。
- `Get<T>()` 无参、恒取本 Actor 的 Info（唯一归属由 API 形态强制，见 7.1），Job 无法通过无参 Get 获取非本 Actor 的数据本体；跨 Actor 访问统一走带参 `Get<R>(actorId)` 返回的 Radio 壳——`radio.info.*` 只读快照（`[Fetchable]` 子集）+ `radio.behavior.*` 方法即 `Call`（目标 Actor 自己执行，见 9.3）。
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

    [Projector]
    public int total;
}
```

字段语义：

| 声明 | 语义 |
|---|---|
| `[Persistent, Projector]` | 写盘并推送 |
| `[Persistent]` | 只写盘，适合敏感字段 |
| `[Projector]` | 只推送，适合派生或运行时字段 |
| `[Fetchable]` | 允许其他 Actor 通过 Radio 壳只读访问（生成 `radio.info.*` 只读壳属性；可与 Persistent/Projector 组合） |
| 无声明 | 内部状态 |

实际生成代码可以使用属性或字段包装，但业务必须经过生成的写入口，不能绕过脏标记。

---

## 7. DataStore 与数据温度

### 7.1 DataStore

**挂载与生命周期（裁决④定案，2026-08-03）**：DataStore 挂在 Service 容器上，是"把 BehaviorInfo 串起来"的实体，也是 Actor 唯一的数据挂载点。Job 外建 Actor、挂数据、驱生命周期一律走 Service：

- `var store = service.AddActor(actorId);` 登记虚拟 Actor（ID→DataStore 注册表），重复 ID 抛异常；`service.GetStore(id)` 查询（未登记返回 null）。
- `store.AddInfo<T>();` 显式挂载某个 BehaviorInfo（`new T()` + 注册）。
- `service.Active(id)` / `Deact(id)` / `RemoveActor(id)` 驱动生命周期（两件套），触发该 Actor 所有 Behavior 的对应钩子（6.1）；**无 `Load/Unload` API**——数据进出内存由框架在 `Get<T>()` 背后自动做。
- Job 内访问不经过 Service：`JobScheduler.Post` 已把 Job 直连到 store（`engine.service.GetStore(actorId)`），`JobContext.Get<T>()` 恒取本 Actor 数据（经 `current.data` 直达）。
- **Service 不对 Behavior 暴露（决策 #51）**：Behavior/Job 在编译期无法感知 Service 下其他 Actor——Job 内唯一数据入口是 `JobContext.Get<T>()`（本 Actor，IWaitable）与带参 `Get<R>(actorId)`（跨 Actor Radio 壳），`engine.service` 只存在于 Engine 的调度/装配层（`JobScheduler.Post`、生命周期驱动、`AddActor`），不进入 Behavior 注入面。

```csharp
// 无参：恒取当前 Job 所属 Actor 的 Info（IWaitable：命中即续、未命中自动挂起加载）
IWaitable Get<T>() where T : BehaviorInfo;
// 带参：其他 Actor 的 Radio 壳引用（O(1) 零拉取；info 子壳按需拉取 + behavior 内部 RPC）
R Get<R>(ulong actorId) where R : Radio;
// 标记本 Actor 待写回
void MarkSave();
```

`DataStore` 的本 Actor 入口**无参、恒作用于当前 Job 所属 Actor**——唯一归属（1.3）由 API 形态强制：Job 无法通过无参 `Get<T>` 获取非本 Actor 的数据本体；跨 Actor 访问统一走带参 `Get<R>(actorId)` 返回的 **Radio 壳引用**（O(1) 零拉取；`radio.info.*` 子壳用才拉、`radio.behavior.*` 内部 RPC 天然按需，见 9.3）。无参与带参由类型参数区分：`where T : BehaviorInfo` 取数据（可变），`where R : Radio` 取壳（只读）；must 临界区内的参与方字段访问走框架注入的专用入口（9.2），不经过 DataStore.Get。

**`Get<T>()` 是 IWaitable（2026-08-04 定案，方案 A）**——`yield return ctx.Get<T>()`，与 Radio 子壳 `yield return radio.info.bag` 完全同形态，业务写法统一：

- 命中内存时，`Get<T>` 立即续跑，等价 O(1) 查询。
- 未命中时，框架自动创建 `WaitForLoad` 挂起，IO 层异步读取，完成后通过 MPSC 唤醒 Job——**开发者不感知"加载"这件事**（虚拟化承诺：actor 存在 = 数据存在）。
- 同一 Actor/BehaviorInfo 的并发加载必须合并，不能重复打 MongoDB。
- 加载失败、超时、Actor 销毁和迁移都必须唤醒并结束等待 Job；"取消"仅来自生命周期事件（销毁/迁移），**玩家下线不取消挂起 Job**——业务逻辑与在线状态无关，离线所需的 BehaviorInfo 走按需加载路径，Job 照常推进（3.2）。
- **无 `Load<T>()`/`LoadAll()` 显式加载入口**：加载是框架在 Get 背后的内部行为，业务没有"手动加载/全量加载"动作（2026-08-04 撤销）。

挂起点必须是调度器可见的 `WaitForLoad`：业务写法可以保持连续，`Get<T>` 不在同步代码中隐式阻塞或隐藏 IO。

### 7.2 数据温度

冷热卸载的单元是 BehaviorInfo 本身（不是 Actor），判定维度是时间跨度：每个持久化 BehaviorInfo 记录最近访问时间，超过阈值即进入冷处理。Actor 壳（身份、路由）常驻，不参与冷热：

```text
Hot  ：在线会话正在读写，驻留内存
Warm ：最近被访问过但已闲置，超过时间阈值 → 内存卸载：未落库的 dirty 先由 Truck 写回，
       DB 文档保留；再次访问按需懒加载
Cold ：长期无人访问，DB 文档常驻，内存不保留副本
```

卸载判定为双重条件：`lastAccessAt` 超过时间阈值，且 Job 引用计数为零（Job 通过 Get 引用的 `BehaviorInfo` 在 Job 存活期内受保护、不可卸载）。卸载动作在内存层完成，MongoDB 文档是持久化真相、始终存在。大型低频 BehaviorInfo（背包、邮件）是冷热卸载的主要受益者，小型高频字段（等级、坐标）天然常驻。

**`lastAccessAt` 语义（R7 定案）**：数据只被 Job 触碰——Job 通过 `DataStore.Get` 读写该 `BehaviorInfo` 时刷新其内存中的 `lastAccessAt`，**读和写都算访问**（读=Get 加载时刷新，写=提交时刷新）。该字段**只维护在内存，不落 DB**：读刷新不产生任何脏写，规避读放大；卸载时随之丢弃，重新加载后从加载时刻重新计时。**在线不等于数据被保鲜**：玩家在线仅表示 Actor 壳常驻，闲置 BehaviorInfo 无 Job 触碰、时间戳老化，照样 Hot→Warm→Cold 卸载。重启后 lastAccessAt 从加载时刻起算，接受冷启动偏差（不影响业务正确性，仅影响首轮卸载时机）。

---

## 8. 持久化、脏标记与投影

### 8.1 脏标记语义

脏标记表示"需要向外投影的变化"，不承担全局回滚日志职责：

- 标量 setter 置 `projectDirtyMask`。
- 容器的 `Set/Add/RemoveAt` 记录容器差异并置位。
- 批处理边界收集差异后清理投影脏标记。
- 失败 Job 的回滚同时清理本 Job 产生的投影变化。

### 8.2 Projector

投影收集发生在 Job 提交后的调度轮次边界：

```text
Actor / BehaviorInfo
  → 检查 projectDirtyMask
  → 收集标量值和容器差异
  → Projection Rules 裁剪/格式化
  → 查 Compass 取投影目标（见下）
  → Transport 发送 ProjectorPacket
```

**投影目标寻址（R3 定案）**：Service 不维护客户端 session，投影投递复用 4.2 既有路由——`online:{actorId}` 记录 `{serviceAddr, gatewayAddr}`，其中 `gatewayAddr` 即玩家当前连接所属 Gateway 实例。投影时 Service 查 Compass 取 `online:{actorId}.gatewayAddr`，将 `ProjectorPacket` 发往该 Gateway，由 Gateway 用本地 session 直发客户端。**无需新增连接映射表**：`gatewayAddr` 本身就是 session 归属映射，映射实体由 Gateway 自持，Compass 数据面只需保留现有 `online:{actorId} → gatewayAddr`。离线 Actor 无 `online` 路由 → 投影挂起或丢弃，重连时 Gateway 请求全量快照（5.1）。

**投影与冷热卸载（交叉点）**：冷热卸载（7.2）不影响投影。投影 diff 产生于 **Job 提交**（8.1：写入动作当场记录差异并置 `projectDirtyMask`），是写入动作的副产品，而非"内存状态 vs 客户端基线"的差值计算——卸载掉的是内存副本，不是产生 diff 的能力。主循环内投影收集（`CollectProjection`）严格先于卸载判定（`EvictColdData`，11 章）：同一轮中 Job 提交修改 → 投影整包发出并清投影脏标记 → 数据闲置超阈值才进入卸载候选，**不存在"有未发 diff 却被卸载"的窗口**。被卸载的数据再次被 `Get<T>()` 触碰时自动懒加载（7.1），修改后 Commit → diff 照常收集照常发送，业务与投影系统对"是否曾被卸载"无感。离线无路由时 diff 丢弃、重连走全量快照（见上），全量快照数据来源 MongoDB（8.4）与冷卸载落库（7.2）同源——冷卸载不但不破坏全量恢复，反而是全量恢复的数据基础。

**投影打包与顺序（R8 定案）**：一次 Job 提交 = 一个投影整包，包内含该提交改动的**所有** BehaviorInfo 的 diff（钱包、背包同包下发）。客户端以包为原子接收单元**整体应用，绝不部分应用**——从机制上杜绝"钱包扣了、背包没加"的中间帧。包与包之间按提交顺序发送，客户端按接收顺序应用。`ProjectorPacket`、容器差异和临时集合使用对象池或 `ArrayPool`。投影协议必须带：

```text
commitId（提交序，单调递增）+ actorId + 包内 diff 列表（每个 diff：behaviorInfoType + payload）
```

客户端或 Gateway 发现 `commitId` 不连续（缺包）时请求全量快照，不继续盲目应用增量。投影传输走可靠通道（TCP/KCP 自带有序与重传），应用层不实现 ack/滑动窗口；`commitId` 校验作为兜底，仅当通道异常或应用层乱序导致不连续时才触发全量恢复。**不再需要每个 BehaviorInfo 独立的投影版本**：一致性对齐收敛到包级，比逐 BehaviorInfo 追踪版本更简单。

**重启语义（R18 定案，重启 = 全量基线重置）**：进程重启后内存中的提交序列丢失（投影不持久化、Mongo 是唯一真理），增量连续性无从继续——**全量基线重置是必然语义**，不做跨重启 commitId 单调（时间戳/实例 id 组合无法避免全量，只会把"受控通知"退化成"各客户端盲目探测"）。定案：

- Service 重启后 `commitId` **归零重新计数**；Gateway 通过 Service 实例标识变化（注册 Compass 的实例 id）检测会话重置。
- Gateway **主动通知**受影响连接"丢弃旧 commitId 记忆"；客户端收到后丢弃旧 commitId（不再与新实例 commitId 比对），统一请求全量快照重建基线，全量包带新基线 commitId。
- 全量是**受控一次性**：由 Gateway 统一触发、按连接有序下发，杜绝"各客户端各自发现 `5→1` 不连续、各自盲目全量"的随机风暴。
- 重启全量与重连全量（5.1）走同一条路径，客户端无需区分触发原因。

**投影与事件通知 RPC（R8 补充）**：投影整包只负责**状态正确性**——客户端应用后数据即正确，但 diff 本身不表达"发生了什么"（金币变化可能是交易成功、也可能是系统补发）。因此同一 Job 提交在投影整包之后**尾随一个事件通知 RPC**：与投影走同一可靠通道、先投影后事件按序发送，客户端**先应用状态、收到事件后再触发表现**（飘字、音效、动画等时机表达）。事件 RPC 只携带事件语义（事件类型 + 引用参数），**不承载状态真相**（数据已在投影中）——丢失只损失表现时机、不影响状态正确性，重连全量快照兜底后**不重放事件**。

**事件通知 = 一次性单向通知（R19 定案，无 eventId）**：事件通知 RPC 是**服务器 → 客户端的一次性通知（fire-and-forget）**——**无 eventId、无应用层重试、无去重**，一次发起即一次送达，**重复不存在**：at-least-once 重试只属于请求-响应式 `Call`（9.3，服务端等待结果、超时才重试）；事件通知发出即弃、不等待不确认，应用层重试前提不存在、重复前提随之不存在。**但传输层丢包重传保留**：事件通知与投影走同一可靠通道（TCP/KCP 自带有序与重传，8.1）——普通丢包由传输层自动重传解决，不丢事件；**只有不可抗力才真正丢包**（连接断开、进程崩溃、出站背压主动丢弃，见下），此时代价已闭环：仅损表现、投影全量兜底、不重放。

**出站背压（慢客户端）**：3.4 三道限额全在 Service **入站**侧；对称方向——Gateway → 客户端**出站**同样必须有界，否则慢客户端收不动时 `ProjectorPacket` 在 Gateway 发送缓冲无界堆积 = Gateway 内存炸弹。定案：

- Gateway 每连接发送缓冲上限（字节/包数，默认值可配）。满 → **丢弃增量投影与事件通知**：客户端收到不连续 `commitId` 自动请求全量快照（8.2），事件仅损表现——丢弃是安全的，全量恢复机制天然兜底。
- 持续落后（缓冲反复打满）→ **断开连接**，客户端按断线重连/重登流程走全量快照恢复。
- 入站背压防 Service 被刷爆（3.4）、出站背压防 Gateway 内存炸弹，同属队列有界原则。

### 8.3 差异容器

- `GBLList/GBLDict`：持久化容器。
- `TGBLList/TGBLDict`：带投影差异追踪的容器。
- 不暴露会造成引用逃逸的可变元素引用。
- 元素使用 struct 或不可变 class，修改采用替换式。
- 深层嵌套拍平成复合 key，或拆成独立 `BehaviorInfo`。

```text
本批 CollectDiff：
  added / updated / removed
```

差异以客户端重放所需为限；是否携带旧值由投影协议决定，容器旧值不承担全局回滚日志职责。

### 8.4 持久化

MongoDB 是最终持久化来源。持久化单元是 BehaviorInfo：每个持久化 BehaviorInfo 对应一个 MongoDB 文档，复合键 `actorId + behaviorInfoType`；`Truck` 批量写入脏 BehaviorInfo 的完整持久化字段，按文档粒度覆盖写：

- 每个文档使用自己的 `version` 条件更新（BehaviorInfo 级乐观锁）。
- 成功写入后递增该文档的持久化版本。
- 条件更新冲突不能静默覆盖，必须进入迁移、恢复或人工处理路径。
- 高频写的背包只重写背包文档，低频邮件文档不被牵动。
- 进程崩溃允许丢失最近一个 flush 周期内的已修改数据；第一版不引入 WAL。
- 数据 Schema 必须带版本，读取时支持懒迁移或显式迁移任务。

**MongoDB 故障降级**：读侧已有路径（7.1 懒加载失败/超时 → 唤醒 Job → Job 失败回滚，恢复后重试）。写侧 `Truck` 批量提交失败按以下降级：

- dirty 进入**本地写缓冲**（有上限，默认值可配）；缓冲未满时业务照常提交（仅持久化延迟），MongoDB 恢复后**按序回放**——内存是最新真相，回放以内存 version 条件写覆盖，冲突走本段既有的迁移/恢复/人工路径。
- 缓冲满 → **拒绝新写**：业务写入返回失败码（只读降级），读路径仍可服务；恢复后回放缓冲、解除只读。
- 一致性语义不变：Service 进程在缓冲积压期间崩溃 → 缓冲丢失，等价于"崩溃允许丢失最近一个 flush 周期"的窗口被拉长，**不新增任何一致性承诺**。

**客户端感知一致性（R14 定案）**：投影与事件在 Commit 后立即下发（8.2），**先于** Truck 落库。进程崩溃时，最近一个 flush 周期内**已下发投影但未落库**的数据会随重启回滚——客户端把"收到投影"理解为**暂态成功**而非持久确认。为压缩该窗口：**高价值写入**（交易、货币变动等）可配置"提交即立即 flush 单文档"（写确认返回后才算 Commit 完成；仅关键路径使用，其余仍走批量 Truck）。对账侧：崩溃回滚的可解释性依赖失败/审计日志（9.2、9.4）与文档 version 记录，客服可查"已下发 vs 已落库"差异。

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

标量字段保存旧值；容器不做整容器深拷贝，按被写索引记录该项旧值（首次写某索引/元素时备份该项），首次写入 O(1) 常量成本，大容器（背包/邮件）不因首次写而整容器拷贝。元素必须不可变或替换式更新，避免通过内部引用绕过快照和脏标记。

本地回滚只覆盖当前 Job 尚未 Commit 的内存状态，不覆盖已经发生的外部副作用。

投影边界 = Job 提交边界：未 Commit 的修改不进入投影收集。跨多个调度轮次的 Job 中途的字段修改直到 Commit 前都对客户端不可见；Job 失败回滚时客户端从未见过中间态，Commit 后才允许该批投影。

### 9.2 must：同 Service 多 Actor 批原子操作

`must` 是框架级原子原语：把"条件校验 + 对多个 Actor（N 边）BehaviorInfo 字段的修改"声明为一个整体，由 Service 底层在一个不挂起的临界区内一次跑完所有参与方。它是唯一允许在临界区内直写多个 Actor 字段的入口。参与方可以是离线 Actor：虚拟化 Actor 本就具备离线读写，must 按同一路径操作离线参与方的 BehaviorInfo。

适用范围是必须整体原子的 N 边操作（如交易双方同域结算）。广播型批量发放（如 club 给全成员发奖励）不属于 must 场景，统一形态见 9.5。

```csharp
// 编译期定义固定语义（谁扣谁得写死在类里），运行时只传参数，不传输操作逻辑
public sealed class MustTransferGold : Must
{
    public int amount;
    // 同 Service 内 N 边参与方
    public string target;
}
```

调度与执行约束：

- 仅限同 Service 进程内发起，跨 Service 请求不投 must，走普通幂等消息（见 9.4）。
- 投递到 Service 级 must 队列（每 Service 一条，must 属 N 边临界区、不属于单个 Actor）；调度器每轮调度循环从 must 队列取固定配额 `MUST_BUDGET_PER_FRAME` 执行，与各 Actor 普通 Job 交替推进，must 队列内部按到达顺序 FIFO。
- must 有独立配额，与普通 Job 互不挤占：恶意高频 must 只能占 must 队列配额，不能饿死或拖延任何普通 Job。延迟有界：最坏约为待处理 must 数 / 配额 个调度轮次。原子性不变：must 执行仍是不挂起临界区，一次跑完所有参与方。
- 执行体禁 `yield`、禁 DataStore 读、禁循环、禁一切消息投递，只操作已在内存的字段，O(1) 完成（非阻塞日志写入除外）。参与方字段通过**框架注入的专用访问入口**操作，不经过 `DataStore.Get`（must 属 N 边临界区，访问入口由 must 上下文提供）。离线参与方的 BehaviorInfo 在 must 调度前按虚拟化 Actor 离线读写路径加载到位（加载动作在临界区外完成），must 执行时所有参与方字段都在内存。
- **加载失败路径（R9 定案）**：must 类可自定义重试次数与超时（框架给默认值，发起方可覆盖）。参与方预加载失败（超时/IO 错误/参与者不可达）时按 must 定义的重试策略在临界区外重试，重试耗尽或超时 → **must 整体失败**（全败）：零副作用返回失败原因码，发起方按错误码决定重试或放弃。**must 只有全成或全败两种结果**，不存在部分成功：加载阶段重试只是延迟进入临界区，不改变"要么一次跑完所有参与方、要么零副作用回滚"的原子语义。
- N 边在一个临界区内依次应用：单线程下无真并行，但不挂起则外部观察不到任何中间态，效果等价于同时进行。
- **执行期间无并发 Job（必读，消除伪并发疑问）**：must 的发起是同步原子动作——发起方 Job 执行到 must 语句时让出控制权，must 一次跑完全部参与方字段后才返回，期间不 yield、不调度任何其他 Job；因此不存在"must 执行中参与方有挂起 Job 在运行"的窗口（挂起 Job 恢复只能发生在 must 完成之后的调度轮次）。时序上后写者赢：must 完成后再被参与方 Job 修改的字段以 Job 值为准（符合串行因果），must 改过而 Job 未碰的字段保留 must 结果（Job 快照回滚只作用于 Job 自己写过的字段）。
- 任一参与方条件不满足或执行异常 → 整体失败：快照回滚覆盖所有参与方字段，零副作用，返回值带失败原因码（哪个参与方、哪个条件未满足）；全部成功 → 所有参与方看到一致结果。失败时 must 内可写非阻塞日志，记录与原因码同源的失败细节。
- 发起后由 must 内部保证一切业务完成，发起方零确认逻辑，需要时直接读返回值。不自动重试，重试与失败提示属发起方业务。
- 原子性由"同 Service 多 Actor 临界区 + 独立队列配额 + 禁挂起"构成，不依赖锁或冻结标记。该原子性是运行时内存态语义：崩溃时 Truck 逐文档落库在极端情况下可能部分提交，不在 must 承诺内，由日志审计 + 业务补偿兜底。

**must 完成 = 一次原子提交（R17 定案，投影与事件由 must 内部完成）**：must 全部参与方成功执行后，框架在 **must 内部**完成投影收集与事件发送，发起方与参与方业务**零代码**：

- **投影**：各参与方字段变更按各 Actor **各自投影整包**下发（各参与方各自 `commitId` 推进、客户端各自整体应用），复用 8.2 打包语义；收集发生在 must 完成后同一调度轮次边界。
- **事件**：全部参与方投影之后，must **内部**按序尾随"完成事件"RPC——复用 8.2"投影整包后尾随事件通知"机制（同通道先投影后事件），事件语义（类型 + 引用参数）由 must 类声明，**不承载状态真相**，丢失仅损表现、不影响状态正确性。
- **时序与失败**：每个参与方的"投影 → 尾随事件"在同一可靠通道上按序送达；全败路径不投影、不事件、零副作用（R9 加载失败语义不变）。
- **发起方保证**：must 返回值即"已完成投影与事件"保证，无需自行收集或发送。

跨 Service 业务中，must 只在各自 Service 内执行本域部分。**数据所有权约束**：需要多方原子变更的业务（交易双方、对局结算），相关状态必须落在同一个 Service 内才能用 must；若状态天然分属不同 Service，回上层调整数据归属（谁的数据放谁身上，见 10.1），而不是用跨 Service 消息串原子性。跨 Service 部分只传通知，不承载状态真相（见 10.2）。

### 9.3 跨 Actor 写入

跨 Actor 读写统一走 Radio 壳（`Get<R>(actorId)`，见下文）：`radio.info.*` 是只读快照；`radio.behavior.*` 上的 `[Remote]` 方法即 `Call`——目标 Actor 自己执行的写操作：

```text
调用方 → Compass 定位目标
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
- 去重记录和结果缓存必须有界：requestId 记录带保留窗口（如 24h TTL 过期删除），结果缓存设 LRU 上限。

**RPC 主体鉴权（R5 定案）**：客户端发起的 `[RpcMethod]`，执行主体恒等于认证 session 所属的 actorId——**由框架注入，客户端无法以他人身份发起调用（自己就是自己，这是鉴权）**。"对哪个玩家操作"是业务需求，作为**业务参数**（如 `targetActorId`）传入，由服务端业务代码做关系/权限/黑名单校验，框架不做业务拦截。`Call`（`radio.behavior.*` 的 `[Remote]` 方法）等服务端间调用（Service → Service）目标由服务端代码指定，属于受信上下文，经 Compass 寻址，不涉及客户端越权。

**Radio 壳（R15 定案，统一跨 Actor 入口）**：跨 Actor 访问统一走 `Get<R>(actorId)` 返回的 **Radio 壳**——SG 生成的强类型 ActorRef。壳树分**两棵独立子树**，**Behavior 与 Info 不强制成对**，按名字独立生成、只生成有声明的一侧：

- **取壳 O(1)、子壳按需拉取**：`Get<R>(actorId)` 是**纯壳引用**——零拉取、零挂起，不预载整棵壳树；`radio.info.bag` 是**子壳获取器**（返回 `IEnumerator`），`yield return` 挂起直到该子树快照就绪，数据量收敛到本次 Job 真正用到的子树。粒度是子壳不是字段（属性 getter 不能 `yield` 的物理约束）；API 形态强制"先确保后读取"——壳类型不暴露裸字段，未加载直接读字段在编译期不存在。
- `radio.info.*`（数据层，多例）：**Info 壳**——只暴露 `[Fetchable]` 标记字段，生成只读属性，数据量 = 标记字段子集快照（非全量 Info）；字段是**弱一致快照**——不保证实时性，跨帧可能过期，适合查看/预览类场景；**子壳幂等缓存**——同一 actor 同一子壳只拉一次，重复 `yield return` 立即返回缓存、不再产生 IO/RPC。
- `radio.behavior.*`（逻辑层，单例）：**Behavior 壳**——只暴露 `[Remote]` 标记的内部 RPC（= `Call`，目标 Actor 自己执行，at-least-once + requestId 幂等），RPC 天然按需、无需预拉；客户端 `[RpcMethod]` **不进壳**，两个标记职责分离。
- **子壳加载成本分层**：同 Service 内存命中 = 同步零网络（复用内存快照）；同 Service 未在内存 = 复用 7.1 懒加载路径挂起 IO 读 MongoDB；其他 Service = 一次 RPC 只拉该 Behavior 的 `[Fetchable]` 子集。
- **强一致读/写**：走 `radio.behavior.*` 上的 `[Remote]` 方法（在目标域执行返回最新），或重新取子壳刷新快照；增量 diff 刷新为后置优化。
- 壳类型由 SG 静态生成，`radio.info.bag.count` 编译期可检查、不可赋值、不可调用未暴露接口——R15 唯一归属防护从"运行时断言"升级为"编译期不存在"。

### 9.4 冻结-确认与补偿

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

原子性定义：所有参与者对外只暴露 `Committed` 或所有参与者最终回到 `Aborted`，中间状态只能表现为 `Pending`/冻结。不承诺 N 个 Job 在物理上同一时刻执行；协调者故障期间事务可以暂挂，但恢复后只能依据已持久化的单一决议继续 `Confirm` 或 `Cancel`。

强事务模式必须满足：

- 每个事务有全局唯一 `transactionId`，每个参与者操作还带 `participantId`。
- `Prepare`、`Confirm`、`Cancel` 和协调者决议全部幂等；超时只表示结果未知，不能直接重做业务操作。
- 参与者在 `Prepare` 后不得消费或转移被冻结资源；冻结状态属于 `[Persistent]` 数据。
- `CommitDecision`/`AbortDecision` 持久化成功后不可逆，协调者重启后按决议恢复事务。
- 客户端只收到 `Pending` 和最终结果；关键资产在 `Confirm` 完成后才产生最终投影。
- 事务超时、参与者永久离线和重试耗尽进入死信/人工介入，不允许静默解冻或静默提交。

强事务是可恢复的可选协议，普通跨 Actor 交互仍使用 at-least-once 异步消息，不自动升级为全局事务。

不使用 MongoDB 多文档事务：事务参与方可能分布在任意 Service 进程/实例上，Mongo 事务无法跨进程协调，也与"不做 WAL、最终一致 + 补偿"的整体取向一致。单 Service 内、所有参与者同库同集合并可接受同步阻塞 DB 写的场景，可将其作为强事务模式的可选实现路径，默认不引入。冻结状态属于 `[Persistent]` 数据，重启后可以恢复。补偿操作必须带原操作 ID，重复执行不能产生额外结果。框架提供 `ICompensatable` 形态和重试模板，业务只实现具体反向操作。

### 9.5 广播型批量发放（全服发奖励）

全服/群体发奖励（运营补偿、活动奖励）基数可达百万级，逐人持久化消息或邮件成本不可接受，不使用逐人写入。发放采用"批次定义 + 领取资格开放"的三层模型：

- **配置层（静态，Luban 配置表）**：奖励内容、开放领取时间、失效时间。服务端与客户端共享，可多年不变。**配置热更语义**：配置带版本 + 原子切换 + 灰度——运行中 Job 在切换点（Job 开始时固定一次版本快照）取新配置，**一个 Job 内只读同一版本**、不混用新旧；版本不匹配的下发客户端按需拉新。
- **批次层（运行时少量文档）**：一次发放一个批次，是"这次活动"的实例，可多期复用同一配置：

```csharp
[Persistent]
public partial class RewardBatchInfo : BehaviorInfo
{
    // 本次发放唯一 id，每期一个
    public ulong batchId;
    // 指向配置层奖励定义
    public int rewardConfigId;
    // 开放领取时间
    public long openAt;
    // 失效时间
    public long expireAt;
}
```

- **玩家层（玩家已有 BehaviorInfo）**：仅增加已领标记集合，只记"领了没"，不存奖励内容：

```csharp
[Persistent, Projector]
public partial class PlayerInfo : BehaviorInfo
{
    // 已领 batchId，几十个封顶
    public GBLList<ulong> claimedRewardIds;
}
```

发放动作：写一条批次文档 + 广播通知，不创建、不修改任何玩家文档（零玩家写入）。玩家领取资格 = 批次存在 && `openAt ≤ now ≤ expireAt` && 未领。在线玩家在发奖广播后于自己 Job 内即时兑奖（原子写入已领标记 + 入账）；离线玩家不触发任何写入，上线/被 `SeekDeep` 离线激活时，登录 Job 用「内存批次表 − 已领集合 = 待领」求差集后逐批兑奖。永远不上线的玩家不领，批次过期作废。

邮箱式展示形态与批次方案同机制：全服邮件为全局一条模板，玩家侧只记 `readIds`/`claimedIds`，玩家打开邮箱时按模板动态渲染、点击领取时才入账。差别仅在展示层与领取触发点，可组合使用（批次定义 + 邮箱入口）。

约束：

- 已领标记按 `batchId` 记录（每期唯一），不按 `rewardConfigId`——同一配置可被多期复用，按配置 id 记录会导致后续批次无法领取。
- 批次带有效期，过期批次从内存批次表移除，未领作废。
- 幂等天然成立：`batchId` + 已领集合，重复广播与崩溃重试不产生额外结果。
- 不入 must（9.2）：逐人独立入账即可，无整体原子必要性。

**批次定时驱动（R12 定案）**：批次的开放/过期由 **Service 级 TimerWheel 全局周期任务**驱动（11 章 `DrainTimers` 是 Service 进程级设施，不依赖玩家在线）：到 `openAt` 广播开放通知、到 `expireAt` 从内存批次表移除，均注册为 Service 级周期回调；与挂在玩家 Actor 上的 Actor 级周期任务（每日刷新等）不同，**全局批次扫描与在线无关**，离线/无人在线时照样由 Service 主循环推进。

**分层原则（R12 定案）**：**Service 级周期任务只做系统维护/基础设施，不做业务**——online 批量续期（4.2）、幂等表 TTL/LRU 清理（9.3）、过期批次移除、开放广播（通知投递）等，全部不改变业务状态；**业务逻辑只发生在 Actor 级**——领取资格判定、入账、每日刷新、buff 到期等全部落在玩家自己的 Actor Job 里。批次扫描的"广播"是通知（不改变状态），"移除过期"是内存治理，均属维护；领取（真正的业务）永远由玩家自己触发。

**硬边界（R12 补充）**：Service 级周期任务**只扫内存批次表本身**（到期广播 / 过期移除），**绝不遍历玩家集合、绝不 `SeekDeep` 激活离线玩家**——离线玩家的待领资格由玩家自己上线/被激活时惰性求差集得出（9.5），不是后台替玩家扫描得出；任何"对批量玩家做点什么"的需求都必须落到玩家自己的 Job（在线广播让玩家自领、玩家激活时自兑），不落 Service 级周期任务。违反此边界 = 百万级离线激活加载风暴。

**领取检测时机（R20 + R22 定案，2026-08-04 修正）**：批次领取是**惰性检测**，不是持续一致性/广播一致性问题——检测挂在**业务 Job 首次触碰玩家数据**的时刻：在线玩家 `OnActive`（进入活跃、登录 Job）、离线玩家被 `SeekDeep` 拉起后，触发它的业务 Job `Get<T>()` 时框架已保证数据在，求差集自然发生（**不依赖生命周期钩子**，无 `OnLoad`），一次求差、逐批兑奖。**在线漏广播者自愈（R22）**：广播是尽力投递（10.2），漏广播的在线玩家不会主动打开邮件/活动面板——故在**既有 Actor 级周期任务（每日刷新 Job，11）顺手求一次差集**，在线漏收者最迟次日自愈；这是"通知/感知失效"时的正确性兜底（保险丝），与广播/轮询提醒并行，非替代。永远不上线的玩家不领、过期作废。**归属与多实例**：批次为 Service 级共享缓存——各实例自载内存副本、Mongo 权威，不参与 Actor 私有归属模型；多实例各广播/各移除退化为通知噪音——开放广播重复仅损表现（R19），过期移除以玩家激活时刻所在实例内存副本为准（一次性快照判定，批次过期作废天然容忍实例间时序偏差），无需唯一 holder、无需跨实例广播仲裁。

**就绪形态与提醒来源（R22 定案）**：领取机制同一套（批次/模板 + 差集 + 惰性校验），就绪时刻分两类。**广播型（未知事件）**：就绪由运营/系统随时触发（临时补偿、事件达成奖励），玩家预先不知——模板/批次发布时实时广播在线玩家"有可领"：Service 向各 Gateway 群发一条轻量通知，粒度 O(Gateway 数) 而非玩家数（复制在 Gateway 连接层，本职能力），只发"未读数+1"小包、内容玩家打开时渲染，代价可忽略；广播为提醒层（10.2 尽力投递），可丢。**计时型（已知时间轴）**：就绪时刻确定性可算（任务冷却、N 分钟后可领）——服务端下发 `readyAt` 绝对时间戳（BehaviorInfo 字段进投影整包），客户端本地展示倒计时，**点击时服务端惰性校验 `now ≥ readyAt`**；服务端**不跑 per-player 定时器**（到点触发仅对在线玩家有意义、数量随玩家数爆炸，是反模式）；**Actor 级 `[TimerWheel]` 声明钟（11）同样不为失活 actor 跑定时器**——定时器随 `OnActive` 挂载、`OnDeact` 摘除，离线欠账由下次激活结算，定时器只是提醒层；"到点自动入账"同样做惰性（下次激活/周期补 `readyAt ≤ now` 未领者），不做到点定时。**通知永远只是提醒层**：正确性靠"打开拉取 / 差集 / 惰性校验"兜底，通知丢失仅损表现。

---

## 10. 跨服务事件与级联

### 10.1 数据所有权原则：跨 Service 不传真相（R2 定案）

**谁的数据，放谁身上。** 任何状态真相必须归属某个 Actor 的 `BehaviorInfo`，存放在它所属的 Service 里：

- 需要多方原子变更的业务（交易双方、对局结算）→ 相关状态必须落在**同一个 Service**，用 `must` 完成（9.2），禁止跨 Service 传递状态。
- 涉及第三方的业务（曝光、撮合、通知）→ 第三方只持有引用信息，不持有真相。
- 如果一个场景"真相不知道归谁"或"必须跨 Service 做原子变更" → 是设计错误，回上层调整数据归属，而不是加补偿机制。

**可行性基础——虚拟化 Actor 离线读写（4.2、9.2）**：真相留在参与方身上，不会因对方离线而不可用。虚拟化 Actor 的离线读写路径（`SeekDeep` 离线激活 + BehaviorInfo 落库）保证参与方即使不在线，`must` 也能在调度前按离线路径将其 BehaviorInfo 捞到内存完成交易——不存在"对方不在线就做不了"的场景。因此真相可以放心归属参与方，第三方（如拍卖曝光）无需持有真相，也无需为离线玩家准备兜底。

推论：**跨 Service 不存在状态转移，只有通知。** 跨 Service 事件不承载真相，因而不需要 outbox 发送端保证——这是 R2 的结论：**outbox 不引入**。

### 10.2 跨服务事件 = 通知（尽力投递，不是 at-least-once 状态消息）

同步路径只做当前请求必须完成的最小业务：校验、修改、Commit 和回复。非关键级联通过事件异步推进：

```text
A Commit
  → InternalEvent：同 Actor 内 Behavior 级联
  → CrossServiceEvent：跨服务通知（尽力投递，不承载真相）
  → 目标 Service 在目标 Actor Job 中处理
```

规则：

- 同 Actor 内事件受 `CASCADE_BUDGET` 限制。
- 同一调度轮次内同类型事件可以合并。
- 跨服务通知必须有 eventId 和幂等消费记录（防重复曝光、重复处理），但不承诺 at-least-once 送达：丢失代价 = 重新曝光/重新查询，接收方通过查询自身状态即可恢复。
- 热迁移前 flush 已提交但未发送的跨服务通知（尽力 flush，丢失可重新曝光）。
- 事件不能被本地 Job 回滚；失败通过重试或补偿处理。
- 极少数不可对账场景（如 9.4 Saga 协调者）由**协调状态持久化 + 超时重扫**恢复——这是局部机制，不是全局 outbox。

### 10.3 跨服务业务解题 SOP（R2 定案附带，可复用）

面对"两个实体之间要发生业务"（交易、竞拍、组队结算等）的固定套路：

1. **列实体**：列出涉及的所有实体，逐个问"这个状态的真相是谁的"——每个真相必须归属某个 Actor 的 `BehaviorInfo`。
2. **判原子**：若双方需要原子变更（资产交换、结算）→ 双方状态必须落在**同一个 Service**，用 `must` 完成；跨 Service 不做原子变更。参与方不在线不构成障碍：虚拟化 Actor 离线读写（9.2）可在 must 调度前捞取离线参与方的 BehaviorInfo，不必等对方上线。
3. **归位**：若状态天然分属不同 Service → 回上层改数据归属（把参与方状态放同一 Service），而不是用跨 Service 消息串。
4. **降级**：涉及第三方（曝光/撮合/通知）→ 第三方只持有引用信息；跨 Service 只剩通知，丢失靠重新曝光/查询恢复。
5. **验证**：若仍存在"真相不知道归谁"或"必须跨 Service 原子变更" → 设计错误，回第 1 步。

示例（拍卖）：

```text
卖家 A 上架：auctioninfo（期望价/状态）记在 A 的 BehaviorInfo —— 真相在 A
买家 B 出价：出价/冻结押金记在 B 的 BehaviorInfo —— 真相在 B
成交：A 与 B 两个 Actor 的 must —— 同 Service 批原子，物品给 B、金币给 A
auction.service：只曝光（橱窗展示、撮合提醒），全程不持有真相
```

---

## 11. 主循环

业务服是事件驱动的，不存在固定帧率，"帧"不作为时间单位（需要固定步长的战斗场景由专门战斗进程负责）。主循环每轮推进一批就绪工作，空闲即睡眠等待唤醒：

```text
while running:
    DrainCallbacks()          // IO、RPC、DB 结果
    DrainTimers()             // TimerWheel
    DrainInternalEvents()     // Actor 内事件，受预算限制
    DriveCoroutines()         // 就绪 Job 推进到 yield
    ProcessActors()            // 公平预算、Commit/Rollback
    CollectProjection()       // 收集并发送增量
    EvictColdData()           // 按时间跨度 + Job 引用计数卸载冷 BehaviorInfo
    PublishCrossServiceEvents()
    TruckCheck()              // 批量持久化
    SleepUntilNextEvent()     // 无固定帧率，空闲睡眠，由 IO/定时器/消息唤醒

FlushAll()
```

TimerWheel 是 **Service 进程级**定时设施，主循环每轮 `DrainTimers()`。周期任务分两种宿主（R12 定案），**分层原则：Service 级只做系统维护/基础设施、不做业务；业务全部在 Actor 级**：

- **Service 级全局周期任务**：注册在 Service 进程上，与任何玩家在线与否无关，离线/无人时照样推进——**只做系统家政**：online 批量续期（4.2）、幂等表 TTL/LRU 清理（9.3）、过期批次移除、开放广播（通知投递）、慢 Job 监控统计等；**不改变任何业务状态**。任何"对批量玩家做点什么"（领取判定、入账、补偿）都禁止在此层出现。
- **Actor 级周期任务**：挂载在具体 Actor 上，跟随 Actor 生命周期，业务逻辑所在——玩家每日刷新、buff 到期、领取资格判定、入账、漏广播批次/邮件差集自愈（R22）等。

**Actor 级声明钟（`[TimerWheel]` 方法级特性，2026-08-04 定案）**：Actor 级周期任务的声明形态 = **方法级 `[TimerWheel(ID)]` 特性**——业务在自己 Behavior 的任意方法（返回 `IEnumerator`、方法名自定、声明 `public`）上挂特性，即声明"该方法参与定时任务 ID"。频率与生效区间由 Luban 配置表 `timer_wheel`（`id / interval_ms / start_at / end_at`，空 = 永久生效）决定，代码不写频率数字：**配置表只给参数、特性只做声明、方法只管到点做什么，三者互不越界，不从配置表生成代码**。装配全自动：**Service 创建事件反射扫描全部 `Behavior` 子类**，收集各自带 `[TimerWheel]` 的方法并建成开放实例委托（启动一次、运行期零反射；启动期校验方法签名须返回 `IEnumerator`，不符装配期即报错）；`AddActor` 自动实例化默认行为集并登记定时候选；`OnActive` 挂钟（jitter 打散）、`OnDeact` 摘钟——**定时器生命周期 = actor 活跃生命周期，绝不为失活 actor 跑定时器**；离线欠账由下次激活（`OnActive` 挂钟后首个周期）或业务 Job 触碰时惰性结算，定时器只是提醒层（与 9.5 计时型就绪同语义）。一个方法一个 `[TimerWheel]`（要双频率就拆两个方法）。**业务零注册**：没有 `AddBehavior`、没有注册调用，Behavior 类只需继承基类 + 可选特性。

停机时先停止接收新请求，再等待或取消可取消 Job，flush 持久化数据和跨服务事件，最后注销 Service 路由。

---

## 12. 运行约束与可行性边界

**时钟统一**：`lastAccessAt`（7.2）、批次时间窗（9.5）、TTL/续期（4.2）等时间敏感判定依赖服务器时钟。所有 Service 以 NTP 对齐；跨 Service 不做强时钟同步假设，时间敏感判定归**权威时钟**（批次时间窗以批次定义的服务端时间为准）。

### 12.1 第一版必须验证

以下能力在第一版必须实现并验证，否则不算完成：

- 先校验后执行，执行阶段不失败。
- Job 级字段快照回滚。
- 冻结-确认（交易等跨 Actor 场景）。
- 幂等补偿（跨服务异步事件的失败处理）。
- at-least-once RPC + requestId 去重（9.3）。
- 跨服务通知尽力投递，丢失可重新曝光/查询恢复（10.2）。
- 投影按 `online:{actorId}.gatewayAddr` 路由到目标 Gateway 并由其 session 直发（8.2）。
- 看门狗心跳超时触发 dump。
- 挂起 Job 引用的 BehaviorInfo 不被冷卸载（引用计数保护）。
- SourceGen golden 快照、增量缓存、产物编译运行三类测试。
- 崩满重连、重试、限流和降级。
- 客户端 RPC 无法以他人 actorId 发起（执行主体由框架从 session 注入，6.1/9.3）。
- Job 无法通过无参 `Get<T>` 获取非本 Actor 的 BehaviorInfo（无参 API 形态强制唯一归属，6.2/7.1）；跨 Actor 访问统一走 `Get<R>(actorId)` Radio 壳（6.1、9.3）：`Get<R>` 为 O(1) 纯引用零拉取，`radio.info.*` 子壳用才拉（`yield` 子壳获取器，同 actor 同子壳幂等缓存只拉一次）且只暴露 `[Fetchable]` 只读字段（弱一致快照）、`radio.behavior.*` 只暴露 `[Remote]` 方法（Call at-least-once 幂等，天然按需），`[RpcMethod]` 不进壳；两子树独立生成，不要求 Behavior 与 Info 成对。
- 高价值写入"提交即立即 flush"路径 + 崩溃窗口客户端感知语义：已下发投影未落库的数据重启后回滚，客户端以暂态成功处理，对账可解释（8.4）。
- `lastAccessAt` 只由 Job 的 Get 读写刷新、只存内存不落 DB；在线玩家闲置数据照样超时卸载（7.2）。
- 一次 Job 提交的投影以整包下发，客户端整体应用不分拆；commitId 缺号触发全量恢复（8.2）。
- 投影整包后尾随事件通知 RPC：客户端先应用状态后触发表现；事件丢失不影响状态正确性（8.2）。
- must 参与方加载失败按 must 定义的重试次数/超时重试，耗尽整体失败返回原因码；must 只有全成或全败（9.2）。
- must 完成后投影与事件由 must 内部发送：各参与方各自整包投影（各自 commitId）+ 全部投影后尾随"完成事件"RPC；全败无投影无事件（8.2、9.2）。
- 每个等待器（WaitForLoad/WaitForRpc）带 deadline，超时=唤醒+Job 失败回滚；挂起期间调度器继续执行其他 Actor 的 Job；玩家下线不取消挂起 Job（3.2/7.1）。
- Redis 故障时 Compass 按陈旧窗口继续寻址，恢复后待同步队列回放；漏续期（Service 崩溃）→ online 过期停止寻址；迁移期间 TTL 保活不中断（4.2/4.3/5.2）。
- 卡死防护边界：等待段超时（deadline → 唤醒 → 回滚）可验证；墙钟预算（MoveNext 段计时 → 慢 Job 记录/告警 → 下一个 yield 点取消回滚）可验证；无 yield 的 CPU 密集段不承诺进程内强制中断，验证路径为专用 Service 进程级杀+重启（崩溃窗口语义）（3.3）。
- 重启后所有连接强制全量：Gateway 检测 Service 实例标识变化、主动通知客户端丢弃旧 commitId 记忆、统一请求全量重建基线；重启全量与重连全量同路径（8.2）。
- 事件通知 RPC 无 eventId、无应用层重试：一次发起一次送达、重复不存在；传输层重传保留（TCP/KCP 自带），只有不可抗力（断连/崩溃/背压丢弃）才丢，仅损表现、投影全量兜底、不重放（8.2）。
- 生命周期两件套：OnActive 进入活跃 / OnDeact 离开活跃（无 OnLoad/OnUnload，数据进出内存是框架内部事务）；批次领取检测 = 业务 Job 首次触碰玩家数据时求差集（在线 OnActive / 离线被拉起后其 Job Get 时）+ 在线漏广播者 Actor 级周期兜底（每日刷新差集，最迟次日自愈），无后台扫描（6.1、9.5、11）。
- Actor 无 class、纯 `ulong` ID：ID→DataStore 注册表由 Service 容器承载（`AddActor`/`GetStore`/`RemoveActor`），数据经 `store.AddInfo<T>()` 显式挂载，活跃生命周期经 `service.Active/Deact` 驱动（无 Load/Unload API）；Job 内 `JobContext.Get<T>()`（IWaitable，未命中自动挂起加载）经 `current.data` 直达本 Actor store（4.1、7.1）。
- Behavior 自动装配与 Actor 级声明钟：Service 创建事件反射扫全部 `Behavior` 子类并收集 `[TimerWheel]` 方法级特性（开放委托、运行期零反射）；`AddActor` 自动挂载默认行为集（无 `AddBehavior`）；在线活跃 `OnActive` 挂钟、失活 `OnDeact` 摘钟，不为失活 actor 跑定时器，离线欠账下次激活惰性结算（4.1、6.1、11）。

### 12.2 GC 与性能

GC 是软目标而不是硬指标。业务服是事件驱动、无固定帧率，不存在"每帧分配量"的自然分母，不设字节级硬预算：

- 保持分配意识：热路径避免明显分配（对象池、struct、缓存复用），但不在字节数上死抠。
- 真实 SLA：长时间压测下的 Gen2 间隔、暂停时间、P99 延迟和吞吐量。

MongoDB 驱动、网络库和序列化库的分配不完全由 Queen 控制，不能把第三方库行为计入"架构保证"。

### 12.3 工程可行性分级

| 项 | 可行性 | 说明 |
|---|---|---|
| BehaviorInfo 级持久化、BehaviorInfo 级 version | 高 | 每文档独立条件更新，无跨文档事务 |
| 投影与协议生成 | 高 | SourceGen + MessagePack，与客户端对齐 |
| Job 回滚、快照回滚 | 高 | 首次写备份，非全量日志 |
| 进程内单线程调度 | 高 | 无锁，只有逻辑复杂度 |
| must 同 Service 原子批 | 中高 | 禁挂起，需要明确代码规范 |
| 冻结-确认 | 中 | 需要协调者、持久化决议、超时和死信 |
| 在线迁移 | 中 | 排他状态机，低并发窗口可接受 |
| 跨进程消息最终一致 | 中高 | at-least-once + 幂等，业务规则必须幂等 |
| 热迁移的跨服务事件 | 中 | 先 flush 后迁移；尽力投递，丢失可重新曝光/查询恢复（10.2） |
| GC 软目标 | 中 | 以真实压测的 Gen2 间隔、暂停和 P99 为准 |
| 高 CCU 容量 | 中 | 受内存、调度吞吐和第三方库开销限制 |

---

## 13. 分阶段落地路线

### Phase 1：运行时骨架（约 4-6 周）

- 单线程引擎、Service 容器（虚拟 Actor 注册表 ID→DataStore）、Behavior/BehaviorInfo 生命周期
- Job 调度、`IEnumerator` 执行、yield 原语
- DataStore 内存查询 + 异步加载 + `WaitForLoad`
- Job 级字段快照回滚
- SourceGen 基础：`[Persistent]`/`[Projector]` 代码生成、脏标记、快照
- 看门狗
- 协议、序列化、投影的字节级基础
- 单元测试与基础压测框架

`IWaitable` 抽象（恢复/超时/取消）在本阶段定义，后续阶段的 `WaitForLoad`、`WaitForRpc` 只追加具体等待类型。

**Queen.Core 工程结构（Phase 1 骨架）**：

```text
Queen.Core/
├── Core/
│   ├── Engine.cs               # 进程运行时宿主：单线程主循环
│   ├── Service.cs              # 虚拟 Actor 容器（ID→DataStore 注册表 + 生命周期驱动）
│   ├── Behavior.cs             # 纯逻辑基类：actorId + 受限门面 + 生命周期钩子
│   ├── BehaviorInfo.cs         # 数据基类：[Persistent]/[Projector] 三用
│   ├── DataStore.cs            # BehaviorInfo 挂载点（纯数据容器）
│   ├── BehaviorAssembler.cs    # #54：Service 创建事件反射扫 Behavior 子类 → 开放委托工厂表
│   └── Radio.cs                # 抽象壳基类（跨 Actor 引用契约，骨架阶段空壳）
├── Scheduling/
│   ├── Job.cs                  # 调度器一等对象：BeginJob→MoveNext→Commit/Rollback
│   ├── JobContext.cs           # Job 内唯一数据入口：Get<T>()/Get<R>（未命中自动挂起加载，无显式 Load）
│   ├── JobScheduler.cs         # 就绪集合 + 预算 + 背压限额 + 慢 Job（含 must 独立队列 + MUST_BUDGET_PER_FRAME）
│   ├── Must.cs                 # 9.2：框架级 N 边批原子原语（声明基类 + 临界区执行 + 全成或全败 + R9 重试 + R17 收尾）
│   ├── MustContext.cs          # must 内 N 边字段专用访问入口（禁 yield/禁 Get/禁循环，O(1) 临界区）
│   ├── TimerWheel.cs           # 时间轮：主循环 DrainTimers，到点投定时 Job 给 JobScheduler
│   └── IWaitable.cs            # 等待原语：恢复/超时/取消
├── Watchdog/                   # 心跳 >500ms 判死 → dump 协程栈
├── Projection/                 # 投影收集（Job 提交边界，Phase 1 骨架）
└── Attributes.cs               # [Persistent]/[Projector]/[RpcMethod]/[Remote]/[Fetchable]/[TimerWheel]
```

must 与 Job 同级的一等调度对象：Service 级独立队列 + FIFO + 配额由 `JobScheduler` 调度（决策表 #36），执行形态禁 `yield`（非 IEnumerator、临界区直跑），故独立于 `Job` 单独成文件；must 参与方字段走 `MustContext` 专用访问入口，不经过 `DataStore.Get`（#43）。

**退出条件**：Actor 串行性、yield-resume、慢 Job、异常隔离和看门狗心跳超时触发 dump 测试通过。

### Phase 2：持久化与同步（约 4-6 周）

- MongoDB 集成、文档模型、BehaviorInfo 级 version 条件写
- Truck 批量持久化
- Projector、差异容器、池化
- 离线 Actor 访问、`SeekDeep` 激活
- SourceGen 全面完善
- 背压与队列限额完善：Job 限额、must 限额、PPS 限制（3.4）

**退出条件**：随机修改、失败恢复、重启加载和条件写冲突测试通过。Persistent SourceGen 配套三类测试：golden 快照对比（生成代码与标准答案逐字节比对）、增量缓存验证（未改动输入不重新生成）、生成产物可编译可运行。

### Phase 3：单机联调（约 4-6 周）

- 网络层、协议层、Gateway 单机版
- **Gateway 公网边界 KCP 加密**（R11 定案：公网仅 Gateway 客户端侧 KCP，内网全明文；加密随网络层一并落地，死线 = 首次公网部署前）
- 离线交互、跨帧 Job、重连
- 冻结-确认、补偿框架
- 完整游戏循环 demo

### Phase 4：多进程分布式（约 4-6 周）

- Compass、Gateway 多实例
- Service 间 RPC、消息路由、幂等去重
- 在线迁移、版本化寻址
- 分布式运维、监控、日志链路

### Phase 5：压力与可靠性（约 3-4 周）

- 压测、GC 调优、内存监控
- 崩溃恢复、重启、备份、恢复演练

### Phase 6：运维与治理（约 3-4 周）

- 配置中心、发布系统、监控告警
- 死信、人工介入、Schema 迁移
- 协议兼容、灰度、回滚

故障注入（FaultInjector）基建：支持杀 Service 进程、MongoDB 主从切换/断连、消息乱序丢失、网络分区等剧本化演练，验证自愈与账目一致。优先级低于本阶段其余项，可在 Phase 6 后期补齐。

**原则**：Phase 1-3 证明运行时和数据模型；Phase 4-6 才扩展分布式边界，不同时实现全部目标。

---

## 14. 设计决策摘要

| # | 决策 | 方向 |
|---|---|---|
| 1 | 业务代码模型 | 进程内单线程 + 协程交替，不用 async/await 做 Job 模型 |
| 2 | 状态模型 | 一份 `BehaviorInfo` 同时承载业务状态、持久化、投影 |
| 3 | Actor 并行 | 不同 Service 实例并行，单实例内协作式串行 |
| 4 | 数据温度 | BehaviorInfo 级冷热卸载，`lastAccessAt` 时间跨度 + Job 引用计数双判定 |
| 5 | 持久化单元 | 每 BehaviorInfo 一个 MongoDB 文档，复合键 `actorId + behaviorInfoType` |
| 6 | 版本粒度 | BehaviorInfo 级 version 乐观锁，按文档粒度条件更新 |
| 7 | 脏标记 | 只推送不全局回滚；Job 级字段快照回滚，scope 限于 Job 内 |
| 8 | 回滚范围 | 先校验后执行 + Job 快照 + 冻结-确认 + 幂等补偿四件套 |
| 9 | 跨 Actor 写 | Radio 壳（#44）：`radio.info.*` 只读快照 + `radio.behavior.*` 方法即 `Call`（目标 Actor 执行） |
| 10 | 同 Service 原子批 | `must` 原语，独立配额、禁挂起、快照回滚 |
| 11 | 跨进程一致性 | at-least-once 幂等消息 + Saga；关键资产用 Prepare/Commit/Confirm |
| 12 | 多文档事务 | 不使用 MongoDB 多文档事务，最终一致 + 补偿 |
| 13 | 广播型批量 | 批次定义 + 领取资格开放，零玩家写入，不走 must（9.5） |
| 14 | 迁移 | 排他状态机，冻结新写、flush 后激活 |
| 15 | 离线激活 | 可感知延迟流程，`SeekDeep` 触发，按需加载 |
| 16 | 投影 | Job 提交边界收集整包，`commitId` 包级连续性校验，缺号全量恢复（8.2） |
| 17 | 崩溃恢复 | 无 WAL，接受丢失最近一个 flush 周期内的数据 |
| 18 | 幂等表 | 有界：TTL 保留窗口 + LRU 缓存上限 |
| 19 | 卡死防护 | Analyzer + 慢 Job 监控 + 运行时看门狗三层 |
| 20 | GC | 软目标、不设字节硬预算；性能以真实压测的 Gen2 间隔、暂停和 P99 为准 |
| 21 | 帧模型 | 业务服事件驱动、无固定帧率；固定步长战斗由专门战斗进程负责 |
| 22 | 故障注入 | Phase 6 后期引入 FaultInjector 剧本化演练，低优先级 |
| 23 | SourceGen 质量 | golden 快照 + 增量缓存 + 产物编译运行三类测试入 Phase 2 退出条件 |
| 24 | 数据所有权 | 谁的数据放谁身上；多方原子变更必须同 Service 用 must；跨 Service 只传通知不传真相（10.1） |
| 25 | 跨服务事件保证 | 通知尽力投递 + 重新曝光/查询恢复，不引入 outbox（10.2） |
| 26 | 投影寻址 | Service 查 `online:{actorId}.gatewayAddr` 路由投影到目标 Gateway 直发，不新增映射表（8.2） |
| 27 | 调度容量 | 就绪集合（ready set）结构性先行避免每轮 O(N) 全扫；容量数字以 Phase 5 实测校准，不拍脑袋（R4） |
| 28 | RPC 鉴权 | 执行主体由框架从 session 注入恒为自己；目标玩家是业务参数由业务鉴权；服务端间调用受信（6.1、9.3） |
| 29 | 在线路由与降级 | online 由 Actor 宿主 Service 续期（≤TTL/2 批量）；Compass 内存镜像 + 陈旧窗口 + 待同步回放；迁移显式保活（4.2/4.3/5.2） |
| 30 | lastAccessAt 语义 | 读写都算访问（Job Get 触发）；只存内存不落 DB；在线不保鲜、闲置照样卸载；重启从加载时刻计时接受偏差（7.2） |
| 31 | 投影一致性 | 一次 Job 提交 = 一个投影整包（含全部 BehaviorInfo diff）；客户端整体应用绝不分拆；commitId 包级连续性校验，缺号全量（8.2） |
| 32 | 投影与事件通知 | 投影整包=状态正确性；尾随事件 RPC=时机表达（同通道有序：先应用状态后触发表现）；事件不承载真相，丢失仅损表现、全量兜底不重放（8.2） |
| 33 | must 加载失败 | must 类自定义重试次数+超时（默认值可覆盖）；预加载失败临界区外重试，耗尽/超时整体失败返回原因码；must 只有全成或全败（9.2） |
| 34 | 等待超时与取消 | 每个等待器必须有 deadline（默认值可覆盖），超时=唤醒+Job 失败回滚；挂起让出不堵 Service；取消链仅由销毁/迁移触发，玩家下线不取消 Job（3.2/7.1） |
| 35 | 加密边界 | 公网边界=Gateway 客户端侧且仅 KCP 走公网；Gateway 公网侧 KCP 数据面加密（协议层对称加密，密钥随认证协商）；内网（Gateway↔Service、Service↔Service）全明文（5.1、13 Phase 3） |
| 36 | 背压与队列限额 | 三类队列（IO MPSC/Actor Job/加载唤醒）全部有界；Job 限额（Actor 级队列上限，满→拒绝+busy 码）、must 限额（独立队列+MUST_BUDGET_PER_FRAME，9.2）、PPS 限制（每 Actor 令牌桶，超限拒绝+告警）；满策略默认拒绝+告警、丢弃仅限可丢场景；随调度器 Phase 1 骨架落地、Phase 2 完善（3.4） |
| 37 | 顶号与会话并发 | 单会话语义：session 带 epoch，认证递增即撤销旧 session 与 resumeToken；online 映射指向新 Gateway 后投影自然转向，旧 Gateway 残留投影随撤销丢弃；被顶号旧端重连只能重新登录（5.1） |
| 38 | MongoDB 故障降级 | 写侧 Truck 失败 → 本地写缓冲（有上限）→ 满则拒绝新写只读降级 → 恢复按序回放（内存 version 覆盖）；崩溃丢缓冲=flush 窗口拉长，不新增一致性承诺（8.4） |
| 39 | 出站背压 | Gateway 每连接发送缓冲有上限，满→丢弃增量投影+事件（commitId 缺号自动全量兜底）；持续落后→断开重连走全量（8.2） |
| 40 | 配置热更 | 配置带版本+原子切换+灰度；Job 开始时固定版本快照，一个 Job 内只读同一版本（9.5） |
| 41 | 时钟统一 | NTP 对齐；时间敏感判定（lastAccessAt/批次时间窗/TTL）归权威时钟，跨 Service 不做强时钟同步假设（12） |
| 42 | 客户端感知一致性 | 收到投影 = 暂态成功 ≠ 持久化确认；高价值写入可配置"提交即立即 flush 单文档"；崩溃回滚可对账解释（8.4、12.1） |
| 43 | DataStore 归属强制 | 本 Actor 入口（`Get`/`MarkSave`，`Load`/`LoadAll` 已撤销见 #55）全部无参、恒取当前 Job 所属 Actor；跨 Actor 访问统一走 Radio 壳 `Get<R>(actorId)`（`radio.info.*` 只读 + `radio.behavior.*` = Call，见 #44）；must 参与方字段走框架注入专用入口（6.2、7.1、9.2） |
| 44 | Radio 壳 | 跨 Actor 统一入口：`Get<R>(actorId)` 返回 SG 强类型壳引用（O(1) 零拉取），分 `radio.info.*`（子壳按需拉取，只暴露 `[Fetchable]` 只读字段，弱一致快照，同 actor 同子壳幂等缓存）与 `radio.behavior.*`（只暴露 `[Remote]` 内部 RPC，Call，天然按需）两棵独立子树；Behavior 与 Info 不强制成对，按名字独立生成；客户端 `[RpcMethod]` 不进壳（6.1、7.1、9.3） |
| 45 | must 提交/投影/事件 | must 全部成功 = 一次原子提交：各参与方按 Actor 各自投影整包（各自 commitId 推进），全部投影后 must 内部按序尾随"完成事件"RPC（语义由 must 类声明，不承载真相）；全败不投影不事件零副作用；发起方读返回值即获保证（9.2、8.2） |
| 46 | commitId 重启语义 | 进程重启 = 全量基线重置：commitId 归零重新计数；Gateway 检测实例标识变化主动通知客户端丢弃旧 commitId 记忆、统一请求全量重建基线；不做跨重启单调（增量无从继续，只会把受控全量退化成盲目探测）（8.2） |
| 47 | 事件通知 RPC | 服务器→客户端一次性单向通知（fire-and-forget）：无 eventId、无应用层重试、无去重，一次发起一次送达、重复不存在（at-least-once 重试仅属请求-响应式 Call）；传输层重传保留（TCP/KCP 自带），只有不可抗力（断连/崩溃/背压主动丢弃）才丢，仅损表现、投影全量兜底、不重放（8.2） |
| 48 | 生命周期两件套 + 批次领取时机 | 行为生命周期两钩子：OnActive 进入活跃 / OnDeact 离开活跃（无 OnLoad/OnUnload——数据进出内存是框架内部事务，2026-08-04 修正）；批次领取检测 = 业务 Job 首次触碰玩家数据时求差集（在线 OnActive / 离线被拉起后其 Job Get 时）+ **在线漏广播者 Actor 级周期兜底（每日刷新差集，最迟次日自愈，R22）**；就绪时刻分广播型（未知事件，实时广播提醒、可丢）/计时型（`readyAt` 时间戳下发、客户端倒计时、点击惰性校验，服务端无 per-player 定时器）；批次为 Service 级共享缓存非 Actor 归属，多实例广播/移除为通知噪音，无需唯一 holder（6.1、9.5） |
| 49 | CPU 密集 Job 卡死 | 超时检测分两层：等待段 deadline（R10）保证"唤醒+回滚"；无 yield 的 CPU 密集段不超时、看门狗仅 dump。进程内强制中断+回滚不可实现（协作式协程无抢占点、回滚执行不到）；兜底在进程级：CPU 密集工作下沉专用 Service + 外部看门狗杀进程重启 = 崩溃窗口语义（Mongo 真相恢复、<200ms 丢失可接受）（3.3）；**墙钟预算层（R21 追加）**：每轮 MoveNext 段打点计时，超预算记慢 Job + 告警 + 下一个 yield 点协作式取消回滚；只抓"慢但会返回"的段，不抓永不返回的卡死段 |
| 50 | Actor 形态（裁决④，2026-08-03） | Actor 收窄为纯 `ulong` ID、无 class；"串起 BehaviorInfo" = Service 容器的 ID→DataStore 注册表（`AddActor`/`GetStore`/`RemoveActor`），数据经 `store.AddInfo<T>()` 显式挂载；活跃生命周期由 `service.Active/Deact` 驱动（无 Load/Unload API，2026-08-04）；Actor 虚拟化（离线激活/迁移）以此为前提。Behavior 为纯逻辑（注入受限门面见 #51，两钩子），与 BehaviorInfo 不强绑定、无泛型基类（`Behavior<T>` 作废），Job 内取数据一律 `JobContext.Get<T>()`（IWaitable，4.1、6.1、7.1） |
| 51 | Service 不对 Behavior 暴露（2026-08-03） | Behavior/Job 编译期不可感知 Service 下其他 Actor：Job 内唯一数据入口是 `JobContext.Get<T>()`（本 Actor，IWaitable）与带参 `Get<R>(actorId)`（跨 Actor Radio 壳）；`engine.service`（`AddActor`/`GetStore`/生命周期驱动）只存在于 Engine 的调度/装配层，不进 Behavior 注入面；Behavior 注入面 = `actorId` + 受限门面（日志/时钟），waitable/timer 均不入面（6.1、7.1） |
| 52 | Engine/Service 不合并（2026-08-03） | 保持三份独立：Engine（怎么跑：主循环/IO 回收入口/看门狗挂载）、Service（谁是谁：stores 注册表 + 生命周期）、JobScheduler（调度）；合并会让 Behavior 为拿日志而面对装着整个容器注册表的巨型对象、摧毁 #51；1:1 实例比例不构成合并理由（职责维度不同、可独立单测） |
| 53 | Radio 分层（2026-08-03） | Radio 契约（`Radio` 基类 `where R : Radio` + `JobContext.Get<R>(actorId)` 入口）入 Core（与 `Get<T>` 同组归属强制契约，骨架阶段空壳，O(1) 同 Service 内存命中取壳引用）；壳类型（`radio.info.*`/`radio.behavior.*`）由 SG 生成（后置）；子壳拉取/幂等缓存/RPC 投递/寻址后置（Phase 2 懒加载 / Phase 3 网络层）；与 Projection/Redis/Mongo 同模式：契约入 Core、实体后置（6.1、7.1、9.3） |
| 54 | Actor 级声明钟 + Behavior 自动装配（2026-08-04） | Actor 级周期任务声明形态 = **方法级 `[TimerWheel(ID)]` 特性**（方法名自定、返回 `IEnumerator`、`public`）；频率/生效区间由配置表 `timer_wheel`（id/interval_ms/start_at/end_at，空=永久）决定，表只给参数、特性只做声明、方法只管执行，不从配置表生成代码。**Behavior 注册走反射**：Service 创建事件扫全部 `Behavior` 子类（同读 `[TimerWheel]` 注解）→ 建开放委托工厂表（启动一次、运行期零反射；启动期校验签名须返回 `IEnumerator`）；`AddActor` 自动实例化默认行为集并登记定时候选，业务零注册（无 `AddBehavior`、DataStore 回归纯数据容器）；`OnActive` 挂钟（jitter 打散）/`OnDeact` 摘钟，**不为失活 actor 跑定时器**，离线欠账下次激活惰性结算、定时器仅提醒层。一方法一特性（双频率拆两方法）；一进程多 Service 归属后置（当前拓扑每进程一 Service 天然无冲突）（4.1、6.1、9.5、11） |
| 55 | Get 即 IWaitable + 生命周期两件套（2026-08-04） | `Get<T>()` 返回 IWaitable、`yield return ctx.Get<T>()` 与 Radio 子壳同形态：命中立即续、未命中自动创建 `WaitForLoad` 挂起加载——开发者不感知"加载"（虚拟化承诺 actor 存在 = 数据存在）；撤销显式加载入口 `Load<T>()`/`LoadAll()`（加载是框架在 Get 背后的内部行为）；撤销 `OnLoad`/`OnUnload` 钩子与 `Service.Load/Unload(id)` API，激活维度降级为框架内部概念，生命周期只留 `OnActive`/`OnDeact`（6.1、7.1、9.5、11） |
