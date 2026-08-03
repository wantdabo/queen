# Queen v4 实现计划（2026-08-02）

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 按 `Docs/architecture.md` v4.1 从零实现 Queen 运行时：单线程 + 协程（IEnumerator）Actor 框架，BehaviorInfo 三用 + SG 生成，Job 快照回滚，must/冻结-确认，投影整包 + 事件，多进程分布式；以 12.1 验证清单为第一版完成线。

**Architecture:** 进程内单线程协作式调度 Job；数据真相归属 Actor 的 BehaviorInfo（`[Persistent]`/`[Projector]` + SG 生成 `_bak_`/`_dirty_`）；MongoDB 唯一真理（BehaviorInfo 级 version 条件写 + Truck 批量 flush）；投影按 Job 提交边界整包 + commitId 校验；跨 Actor 走 Radio 壳，跨 Service 只传通知；Router 寻址（online 映射 + TTL 续期）。

**Tech Stack:** .NET 8 / C#（无 async）、MongoDB.Driver 2.26、StackExchange.Redis 2.8、LiteNetLib 1.2 + TouchSocket 3.0、MessagePack、Roslyn Source Generator、xUnit。

---

## 0. 前置与现状盘点

- **唯一真理**：`Docs/architecture.md` v4.1（决策表 49 条）；`Docs/design-review.md` R1-R22 已全部击破回写，无遗留 P0。
- **编码规范**：`Docs/CODING_STYLE.md` 硬性约束（camelCase 字段、PascalCase 类/方法、`On` 钩子、SCREAMING 常量、禁 `!`、禁 async、注释独立行中文、零分配、CRLF、4 空格）。改码前先对照严禁清单。
- **可复用基建（业务层已作废）**：
  - `Queen/Core/Engine.cs` 单线程主循环 + `Comp` 组件系统 + `ethread` 线程检查 —— 保留
  - `Queen/Common/`：`Eventor`/`Ticker`/`ObjectPool`/`Logger`/`Config`/`Random`/`Tables`/`DBO`/`MDBO` —— 保留
  - `Queen/Common/Parallel/CoroutineScheduler.cs` 已有 IEnumerator 协程调度雏形（池化）—— 演进为 Job 调度器
  - `Queen/Network/`（Slave/RPC/Adapter/Cross）、`Queen.Protocols`（MessagePack + Gen）—— Phase 3 复用
  - `Queen.Server/`（Server/Settings/System）业务层作废，按 v4.1 重写
- **客户端参考**：goblin 已实现 `GBLList/TGBLList/[Projector]/ProjectorSystem/ObjectCache`，Queen 侧同构移植。
- **新建工程**：`Queen.Generator`（SG 源生成器）、`Queen.Tests`（xUnit 测试工程）。

---

## 1. 里程碑总览（对齐 architecture 13 章）

| 阶段 | 主题 | 周期 | 退出条件 |
|---|---|---|---|
| Phase 1 | 运行时骨架 | 4-6 周 | Actor 串行性、yield-resume、慢 Job、异常隔离、看门狗 dump 测试通过 |
| Phase 2 | 持久化与同步 | 4-6 周 | 随机修改/失败恢复/重启加载/条件写冲突测试通过；SG 三类测试通过 |
| Phase 3 | 单机联调 | 4-6 周 | Gateway 单机 + KCP 加密 + 冻结-确认 + 补偿 + 完整游戏循环 demo |
| Phase 4 | 多进程分布式 | 4-6 周 | Router/Gateway 多实例、Service 间 RPC 幂等、在线迁移、版本化寻址 |
| Phase 5 | 压力与可靠性 | 3-4 周 | 压测 P99/GC 达标、崩溃恢复演练 |
| Phase 6 | 运维与治理 | 3-4 周 | 配置中心、Schema 迁移、协议兼容、灰度回滚、FaultInjector |

**原则**：Phase 1-3 证明运行时和数据模型；Phase 4-6 才扩展分布式边界，不同时实现全部目标。

**12.1 验证清单（24 项）**：运行时语义项 → Phase 1；持久化/投影/卸载项 → Phase 2；网络/跨 Actor/加密项 → Phase 3；分布式项 → Phase 4。逐条映射见各 Phase 任务。

---

## 2. Phase 1：运行时骨架（详细任务）

> 全程 TDD：失败测试 → 实现最小代码 → 测试通过 → 提交。每任务一次提交。

### Task 1.1 项目结构与测试基座
- Modify: `Queen/Queen.csproj`、`Queen.sln`
- Create: `Queen.Tests/Queen.Tests.csproj`（xUnit）、`Queen.Tests/TestInfra.cs`（引擎内存起停、tick 注入）
- Step 1: 新建测试工程并挂入 sln；冒烟测试：Engine → AddComp → Destroy 生命周期正确。
- Step 2: `dotnet test` 通过。
- Step 3: 检查现有 `Engine`/`Comp` 命名合规（`ethreadId` 等已 camelCase），不改语义。
- Step 4: 提交 `chore: scaffold tests + engine smoke`.

### Task 1.2 IWaitable 抽象
- Create: `Queen/Core/IWaitable.cs`（`finished`/`deadline`/`Cancel`）、`Queen/Core/WaitHandle.cs`（`WaitForLoad` 基座）
- Step 1: 失败测试：等待器到 deadline 后 `finished` 置位且调度器可感知。
- Step 2: 实现最小 IWaitable + deadline 驱动。
- Step 3: 通过 → 提交 `feat: iwaitable with deadline`.

### Task 1.3 Actor/Behavior/BehaviorInfo 生命周期
- Create: `Queen/Core/Actor.cs`（壳：id、Job 队列、常驻）、`Queen/Core/Behavior.cs`、`Queen/Core/BehaviorInfo.cs`（仅 public 裸字段 + 特性标记，无方法）、`Queen/Core/Behaviors/PlayerBehavior.cs`（`OnLoad`/`OnUnload`/`OnEnter`/`OnLeave`）
- Step 1: 失败测试：挂载 → 四钩子按序触发（OnLoad→OnEnter→OnLeave→OnUnload），销毁对称。
- Step 2: 实现最小生命周期。
- Step 3: 通过 → 提交 `feat: actor/behavior lifecycle`.

### Task 1.4 Job 调度器（ready set + 单 Actor 串行）
- Create: `Queen/Core/Job.cs`（IEnumerator 执行单元：MoveNext 打点、快照句柄、取消标志）、`Queen/Core/JobScheduler.cs`（ready set 结构性先行，禁每轮 O(N) 全扫；同 Actor 严格串行 FIFO；跨 Actor 协作式切换）
- Modify: `Queen/Common/Parallel/CoroutineScheduler.cs` —— 评估复用或重写（Job 多快照回滚/取消/预算，倾向重写核心、保留池化）
- Step 1: 失败测试：A/B 两 Actor 各发 N Job，断言同 Actor 不并发、跨 Actor 交替；`yield return null` 让出。
- Step 2: 实现 JobScheduler（队列 + ready set）。
- Step 3: 通过 → 提交 `feat: job scheduler per-actor serial`.

### Task 1.5 DataStore 内存查询 + Radio 壳骨架 + 引用计数
- Create: `Queen/Core/DataStore.cs`（本 Actor 入口全无参：`Get<T>()`/`Load<T>()`/`LoadAll()`；O(1) 内存查询；引用计数 +1）、`Queen/Core/Radio.cs`（`Get<R>(actorId)` 返回 SG 壳引用 O(1) 零拉取；`radio.info.*`/`radio.behavior.*` 子树骨架）
- Modify: `JobScheduler.cs`（挂起 Job 跨轮次持引用；计数与 Job 生命周期对齐）
- Step 1: 失败测试：Job 内 `Get<T>()` 读写本 Actor Info；无参 `Get` 无法触达他 Actor；`Load<T>()` 未命中走异步加载路径（先内存模拟）。
- Step 2: 实现 DataStore + 引用计数。
- Step 3: 通过 → 提交 `feat: datastore query + refcount`.

### Task 1.6 Job 级字段快照回滚
- Modify: `Queen/Core/Job.cs`（首次写备份 → `_bak_`；失败 `Rollback`/成功 `Commit`；scope 限 Job 内）
- Create: `Queen/Core/JobSnapshot.cs`、`Queen.Tests/Core/JobRollbackTests.cs`
- Step 1: 失败测试：Job 写 A 字段后失败 → A 恢复原值；成功后保留；未写字段零开销。
- Step 2: 实现 JobSnapshot（先手工 `_bak_`，Task 1.9 SG 生成后自动）。
- Step 3: 通过 → 提交 `feat: job snapshot rollback`.

### Task 1.7 看门狗 + 墙钟预算
- Create: `Queen/Core/Watchdog.cs`（心跳超时 dump；MoveNext 段打点超预算记慢 Job + 告警 → 下一 yield 点协作式取消回滚）
- Modify: `JobScheduler.cs`（看门狗检查点）
- Step 1: 失败测试：单段超预算 → 记慢 Job + 下一挂起点取消回滚；心跳停跳 → dump 触发。
- Step 2: 实现 Watchdog。
- Step 3: 通过 → 提交 `feat: watchdog + budget`.

### Task 1.8 异常隔离
- Modify: `JobScheduler.cs`（Job 抛异常 → 快照回滚 → 记日志 → 不拖垮 Actor/调度器）
- Step 1: 失败测试：Job 抛异常 → 回滚，同 Actor 后续 Job 正常，调度器存活。
- Step 2: 实现 try/catch 包 MoveNext 段。
- Step 3: 通过 → 提交 `feat: job exception isolation`.

### Task 1.9 SourceGen 基础（`[Persistent]`/`[Projector]`）
- Create: `Queen.Generator/Queen.Generator.csproj`（Roslyn IncrementalGenerator）、`BehaviorInfoGenerator.cs`、`Diagnostics.cs`（QN 规则：禁 async、`!`、常量在右）
- Create: `Queen.Tests/Generator/`（golden 快照对比 + 增量缓存 + 产物编译运行——三类测试框架，Phase 2 退出条件，Phase 1 搭好）
- Step 1: 失败测试（golden）：`WalletInfo`（`[Persistent] gold`/`[Projector] hp`）→ 生成代码与手写标准答案逐字节比对。
- Step 2: 实现生成器最小路径（属性 + 脏标记 + `_bak_`/`_dirty_` + Commit/Rollback）。
- Step 3: 增量缓存测试：输入未变不重新生成。
- Step 4: 产物编译运行测试。
- Step 5: 全过 → 提交 `feat: behaviorinfo sourcegen`.

### Task 1.10 协议/序列化/投影字节级基础
- Modify: `Queen.Protocols/`（MessagePack 对齐 SG 字段）
- Create: `Queen/Projection/ProjectorSystem.cs`（骨架：Job 提交边界收集 diff → 整包 → 封包；传输 Phase 3）
- Create: `Queen/Common/GBLList.cs`/`GBLDict.cs`/`TGBLList.cs`/`TGBLDict.cs`/`IGBL.cs`/`ObjectCache.cs`（从 goblin 移植：struct 元素、替换式改、Clone/Reset 池化）
- Step 1: 失败测试：收集 1 个 Job 提交内 2 个 BehaviorInfo diff → 单包序列化 → 反序列化一致。
- Step 2: 移植 GBL 容器 + 池化。
- Step 3: 通过 → 提交 `feat: projection collect + gbl`.

### Task 1.11 Phase 1 退出条件全量验证
- Create: `Queen.Tests/Core/Phase1ExitTests.cs`
- Step 1: 全量验收：Actor 串行性、yield-resume、慢 Job、异常隔离、看门狗 dump。
- Step 2: `dotnet test` 全绿 + `dotnet build` 零告警。
- Step 3: 对照 CODING_STYLE 严禁清单逐条自查。
- Step 4: 提交 `test: phase 1 exit criteria`.

**Phase 1 完成 = 12.1 已验证**：先校验后执行、Job 快照回滚、挂起 Job 引用不被冷卸载（引用计数）、无参 `Get<T>` 唯一归属、`Get<R>(actorId)` 壳形态、等待器带 deadline、看门狗 dump、等待段超时 + 墙钟预算。

---

## 3. Phase 2-6 任务列表（进入前细化）

### Phase 2：持久化与同步（4-6 周，2026-08-03 细化）

> **存储分层决策（2026-08-03）**：`StoreBackend` 抽象（IO 层，允许 async，QN1001 用 `[AllowAsync]` 特性豁免）分离语义与实现——内存后端为主测试路径（可注入故障），`MongoStoreBackend`（MongoDB.Driver）真实实现 + 集成测试（本地无 Mongo 时经 Mongo2Go 拉起临时 mongod，或标记 Integration 可跳过）。payload = SG 生成的持久化字段序列化字节。

- **Task 2.1 存储抽象 + 内存后端**：`StoreKey`（复合键 actorId+behaviorInfoType）/ `StoreDoc`（payload+version）/ `StoreBackend` 抽象（`LoadAsync`/`SaveAsync` 条件写/`DeleteAsync`）/ `MemoryStoreBackend`（可注入写失败）；QN1001 加 `[AllowAsync]` 豁免（IO 层）。测试：Save→Load 一致、条件写冲突返回 false、Delete 后空。提交 `feat: store backend abstraction + memory impl`.
- **Task 2.2 Truck 批量持久化 + IO 泵**：`IOPump`（MPSC 回调泵，Execute 时 drain，IO 完成经此回业务线程）；`Truck`（提交边界登记脏 Info → 周期 flush → 成功清持久化脏 / 失败进本地写缓冲（有上限）→ 恢复按序回放 → 满则拒绝新写只读降级 #38）；高价值写入"提交即立即 flush"（#42）；`Engine.Execute` 挂载 IO 泵与 Truck flush 检查点。测试：聚合只写一次、失败进缓冲、恢复回放、只读降级。提交 `feat: truck batch flush + io pump`.
- **Task 2.3 version 乐观锁冲突路径**：Truck 条件写失败（version 不符）→ 不静默覆盖 → 冲突回调（迁移/恢复/人工路径 #5/6）。测试：两实例模拟先后写冲突 → 后写冲突不覆盖。提交 `feat: optimistic lock conflict path`.
- **Task 2.4 SG 持久化序列化**：生成 `SerializePersistent/DeserializePersistent`（持久化字段读写，零反射）；三类测试（golden/增量缓存/编译运行）。提交 `feat: sg persistent serialize`.
- **Task 2.5 DataStore 真实 IO 挂起**：`Load<T>()` 未命中 → WaitForLoad 挂起 → backend 加载 → IO 泵回业务线程 Complete；同 Actor 同 Info 并发加载合并（7.1）；`LoadAll()` 全量加载（登录/激活）；加载失败/超时/销毁唤醒结束等待 Job。测试：未命中挂起恢复、并发合并只打一次、失败唤醒。提交 `feat: datastore io load suspend`.
- **Task 2.6 冷热卸载**：`lastAccessAt`（内存，Get 加载刷新/提交刷新，不落 DB #30）+ Job 引用计数零双判定（#4）；`EvictColdData` 阶段（所有 Job 处于挂起点）；Hot/Warm/Cold 三态；卸载前 dirty 先 Truck 写回、DB 保留、重访问懒加载。测试：闲置超阈值+零引用卸载、有引用不卸载、在线不等于保鲜、重访问懒加载。提交 `feat: cold hot eviction`.
- **Task 2.7 背压与队列限额（#36，可后置 Phase 3）**：Job 限额（Actor 队列满 → 拒绝+busy 码）、PPS 令牌桶。提交 `feat: queue bounds + pps`.
- **Task 2.8 MongoStoreBackend + 集成测试**：MongoDB.Driver 实现（复合键 `_id`、version 条件更新 `$inc`、覆盖写）；Mongo2Go 拉起真实 mongod 集成测试（复合键/乐观锁/覆盖写）。提交 `feat: mongo backend + integration tests`.
- **Task 2.9 Phase 2 退出条件全量验证**：随机修改、失败恢复（缓冲回放）、重启加载、条件写冲突全通过（12.1 持久化/投影/卸载项）。提交 `test: phase 2 exit criteria`.

**Phase 2 完成 = 12.1 已验证**：BehaviorInfo 级持久化文档 + version 乐观锁、Truck 批量 flush + 失败降级回放、离线懒加载、冷热卸载双判定、SG 三类测试。

### Phase 3：单机联调（4-6 周）
1. 网络层：`Slave`/`Adapter` 复用改造；Gateway 单机版（TCP/KCP/WS）；`resumeToken` 认证会话。
2. KCP 公网加密（#35）：仅 Gateway 客户端侧 KCP 走公网，协议层对称加密，密钥随认证协商；内网全明文。死线 = 首次公网部署前。
3. 离线交互/跨帧 Job/重连：断线重连走全量重建基线；重启后连接强制全量（#46）。
4. must 原子批（9.2）：独立队列、禁挂起、预加载失败临界区外重试、全成或全败（#33/#45）。
5. 冻结-确认（9.4）：协调者、持久化决议、超时、死信。
6. 补偿框架：幂等补偿 at-least-once；幂等表（TTL 窗口 + LRU，有界 #18）。
7. 投影整包 + 事件通知（#31/#32/#45）：commitId 包级校验缺号全量；整包后尾随事件 RPC；must 完成事件。
8. 完整游戏循环 demo：登录 → 进 Game → 背包/金币示例业务。
9. 退出条件：冻结-确认、幂等补偿、at-least-once RPC + requestId 去重、投影路由、崩满重连、客户端 RPC 无法越权（框架注入 actorId）、高价值写入 flush + 崩溃窗口语义、事件无 eventId 不重放、生命周期四件套 + 批次领取（#48）全部验证。

### Phase 4：多进程分布式（4-6 周）
1. Router（Redis）：online 映射 `{actorId}.gatewayAddr` + TTL 续期（≤TTL/2 批量）；内存镜像 + 陈旧窗口 + 待同步回放；漏续期 → online 过期停止寻址；迁移显式保活（#29）。
2. Gateway 多实例：session epoch 顶号语义（#37）；出站背压（发送缓冲满 → 丢弃增量 + commitId 缺号全量兜底 #39）。
3. Service 间 RPC：消息路由、幂等去重、requestId；跨 Service 只传通知不传真相（#24/#25）。
4. 在线迁移：排他状态机、冻结新写、flush 后激活（#14）。
5. 分布式运维：监控、日志链路、优雅停机（先停新请求 → 等/取消 Job → flush → 注销路由）。

### Phase 5：压力与可靠性（3-4 周）
1. 压测框架（`Queen.Bot` 改造）：Job/s、CCU、P99；GC 调优（Gen2 间隔/暂停）；内存监控。
2. 崩溃恢复/重启/备份/恢复演练；Redis 故障降级（陈旧窗口寻址 + 恢复回放 #38）。
3. 容量数字以实测校准，不拍脑袋（#27）。

### Phase 6：运维与治理（3-4 周）
1. 配置中心、发布系统、监控告警、日志链路补全。
2. 死信、人工介入、Schema 迁移。
3. 协议兼容、灰度、回滚。
4. FaultInjector（Phase 6 后期，低优先级）：杀 Service 进程、Mongo 主从切换/断连、消息乱序丢失、网络分区剧本化演练。

---

## 4. 关键设计决策速查（实现时必须遵守）

| # | 决策要点 |
|---|---|
| 1 | 进程内单线程 + 协程，禁 async/await（QN1001） |
| 2 | 一份 BehaviorInfo 三用（业务/持久化/投影） |
| 4 | BehaviorInfo 级冷热卸载，双判定（lastAccessAt + Job 引用计数） |
| 5/6 | 每 BehaviorInfo 一文档，复合键 + version 乐观锁 |
| 7 | 脏标记只推送不全局回滚；Job 级字段快照回滚 |
| 9 | Radio 壳：`radio.info.*` 只读 + `radio.behavior.*` = Call（目标 Actor 执行） |
| 10 | must 同 Service 原子批，独立配额、禁挂起、全成或全败 |
| 11/12 | at-least-once 幂等 + Saga；不用 Mongo 多文档事务 |
| 16/31/32 | 投影整包 + commitId 包级校验缺号全量；整包后尾随事件 RPC |
| 17 | 无 WAL，接受丢失最近一个 flush 周期 |
| 18 | 幂等表有界：TTL + LRU |
| 23 | SG 三类测试（golden/增量/编译运行） |
| 26/29 | 投影寻址 online 映射；Router 内存镜像 + 陈旧窗口 |
| 28 | RPC 主体由框架注入恒为自己，目标玩家是业务参数 |
| 36 | 三类队列全部有界：IO MPSC/Actor Job/加载唤醒 |
| 43/44 | DataStore 归属强制：本 Actor 无参入口；跨 Actor 只走 Radio 壳 |
| 45 | must 提交 = 各参与方各自整包投影 + 全部投影后尾随完成事件 |
| 48 | 生命周期四件套 + 批次领取（激活时刻求差集 + 每日兜底） |
| 49 | CPU 密集卡死：等待段 deadline + 墙钟预算 + 专用 Service 进程级兜底 |

---

## 5. 风险与依赖

- **依赖**：Phase 1 不依赖网络/DB（纯内存即可测）；Phase 2 依赖 MongoDB；Phase 3 依赖 goblin 协议对齐（`Queen.Protocols` 与客户端消息兼容）。
- **风险**：
  - SG 生成器复杂度（脏标记/快照/Radio 壳）→ 用 golden 测试锁住，先最小路径后补全。
  - Job 调度器与旧 CoroutineScheduler 取舍 → 重写核心，保留池化。
  - 单线程吞吐上限 → Phase 5 实测校准，不在 Phase 1 提前优化。
  - 跨 Service 事件一致性 → 遵循"通知不承载真相"原则，宁可丢表现不可坏状态。
- **编码规范**：每任务提交前对照 `Docs/CODING_STYLE.md` 严禁清单（禁 `!`、常量在右、单行卫语句、注释独立行中文、无 emoji、零分配热路径）。

---

## 6. 执行方式

- 计划以 `Docs/plans/2026-08-02-queen-v4-implementation.md` 为单一来源。
- 每 Phase 开始前把该 Phase 任务细化到 bite-size（2-5 分钟动作：写测试/跑红/实现/跑绿/提交），并回写本文件。
- 推荐 subagent-driven 执行：每任务派发新 subagent + 主线程 review，任务间快速迭代。
