# Queen.Core Phase 1 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 从零重建 Queen.Core 运行时骨架（单线程引擎 / Service 容器 / Job 调度 / IWaitable / SG / 投影骨架 / 看门狗），通过 Phase 1 退出条件验收。

**Architecture:** 进程内单线程 + 协程交替（IEnumerator），绝对无锁；数据一份 BehaviorInfo 三用，class 级 `[Persistent]`/`[Projector]` 特性声明、SG 生成实现；跨 Actor 唯一入口 = `Get<R>(actorId)` Radio 壳；must 与 Job 同级的一等调度对象（独立队列 + 配额）。Phase 1 无 MongoDB/Truck/网络/Luban——加载与持久化用内存后端桩闭环，仅验证运行时语义（architecture.md 13 章 Phase 1 + 12.1）。

**Tech Stack:** .NET 10 SDK（C# 14，partial properties 可用）、Queen.Core 程序集（namespace `Queen.Core` 与程序集同名）、xUnit、Roslyn Source Generator（Queen.Generator 独立工程）、Analyzer（QN1001/1002/1003）。

---

## 协作方式（对账制，用户主导）

- 本计划为**对账底稿**：每个 Task 先与用户对齐"做什么 / 结构 / 怎么实现 / 怎么验证"四件事，用户拍板后才进入实现。
- 实现纪律：TDD（先写失败测试→确认失败→实现→通过→提交）、频繁提交、每 Task 一个提交。
- 编码规范（CODING_STYLE.md）：LF 行尾 + UTF-8 BOM、4 空格缩进、camelCase 字段 / PascalCase 类方法 / `On` 前缀钩子 / SCREAMING_SNAKE 常量、常量在左（`if (null == x)`）、禁 `!`、中文独占行注释、Job 返回 `IEnumerator` 且挂起点 `yield return`、禁 async/await。
- 池化已砍（用户裁决）：不做 ObjectCache，GC 为软目标。
- 测试底座：`Queen.Core.Tests` + `[Collection("CoreEngine")]` 串行集合 + 程序集级 `DisableTestParallelization`（防 xUnit 时序偶发失败）。测试桩（Wallet/Bag 等）一律放 `Queen.Core.Tests/` 下，主工程回归纯运行时。
- 复用 2026-08-03 踩坑：SG driver 不可变（结果只在返回值）、增量断言用 transform 计数器、测试传 `CSharpParseOptions(LanguageVersion.Latest)`、AnalyzerReleases 文件防 RS2008、Radio 测试壳需显式 public 构造（`new()` 约束）。

**Phase 1 边界**（不做，留 Phase 2/3）：MongoDB 真实持久化、Truck 批量 flush、version 乐观锁、网络层/Gateway、Luban 配置表（`timer_wheel` 参数默认值占位）、Radio 子壳拉取执行机制、must 参与方预加载重试（R9 只留声明骨架）。

---

### Task 1: 工程骨架与编码规范落地

**Files:**
- Create: `Queen.Core/Queen.Core.csproj`、`Queen.Core.Tests/Queen.Core.Tests.csproj`、`Queen.sln`
- Create: `Queen.Core/Core/Engine.cs`（空壳）、`Queen.Core/Core/Attributes.cs`（`[Persistent]/[Projector]/[Fetchable]/[RpcMethod]/[Remote]/[TimerWheel]/[AllowAsync]` 空 Attribute 定义）
- Create: `Directory.Build.props`、`.editorconfig`

**Step 1: 写失败测试（冒烟）** — `Queen.Core.Tests/SmokeTests.cs`：`using var engine = new Engine(); engine.Execute();` 不抛异常。`TestCollections.cs` 定义 `[Collection("CoreEngine")]`；`AssemblyInfo.cs` 关并行。
**Step 2: 运行确认失败** — `dotnet test`：FAIL（Engine 未定义）。
**Step 3: 实现** — csproj：`net10.0`、`LangVersion latest`、`Nullable enable`、`TreatWarningsAsErrors true`、`RootNamespace Queen.Core`。Engine 空壳（`Execute()` 空实现 + `IDisposable`）。
**Step 4: 验证** — `dotnet test` PASS。
**Step 5: Commit** — `chore: 工程骨架 + 编码规范（Queen.Core/Queen.Core.Tests）`

---

### Task 2: BehaviorInfo 基类 + DataStore 挂载点

**Files:**
- Create: `Queen.Core/Core/BehaviorInfo.cs`、`Queen.Core/Core/DataStore.cs`
- Create: `Queen.Core.Tests/BehaviorInfos/WalletInfo.cs`（测试桩，先非 partial，Task 4 迁 partial）

**Step 1: 写失败测试** — `DataStoreTests.cs`：`AddInfo<T>()` 后 `Get<T>()` 返回同一实例；重复 `AddInfo` 抛异常；`Get` 未挂载抛异常。
**Step 2: 运行确认失败** — `dotnet test --filter DataStoreTests` FAIL。
**Step 3: 实现**
- `BehaviorInfo`：数据基类，类体只声明 public 裸字段（小写 camelCase）；`IsPersistent()/IsProjector()` 静态判定——查字段/属性级特性，**不只查类级**（TruckTests 根因教训）。
- `DataStore`：`Dictionary<Type, BehaviorInfo> infos`；`AddInfo<T>()`/`Get<T>()`；`actorId`；`lastAccessAt` 内存字段（R7：只存内存不落 DB）；Job 引用计数 `refCount`（Task 6 接线）。
- `InfoMeta` 静态缓存：类型 → `[Persistent]`/`[Projector]` 字段集合，运行期零反射。
**Step 4: 验证** — PASS。**Step 5: Commit** — `feat: BehaviorInfo 基类 + DataStore 挂载点`

---

### Task 3: IWaitable + WaitableBase（deadline/取消）

**Files:**
- Create: `Queen.Core/Scheduling/IWaitable.cs`、`WaitableBase.cs`、`WaitForLoad.cs`、`WaitForRpc.cs`

**Step 1: 写失败测试** — `WaitableTests.cs`：超时唤醒（状态=Timeout）；取消走同路径（Canceled）；回调一次性；deadline 有框架默认值、调用方可覆盖（R10）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现**
- `IWaitable`：`WaitableStatus { Pending, Completed, Timeout, Canceled }`；`status`、`deadline`、`Resume(result, status)`、`OnResume(Action)`（单回调，框架内部用）。
- `WaitableBase`：状态机（Pending → 终态一次性）+ 回调唤醒；**超时与取消统一走 `Resume(null, Timeout|Canceled)`**，Job 侧收到即失败回滚（Task 6 接线）。
- `WaitForLoad`（`Type`+结果槽）、`WaitForRpc`（`ulong target`+结果槽）：Phase 1 仅类型与字段，真 IO/RPC 后置。
- 取消链由生命周期事件驱动（Task 9 接线）；玩家下线不取消（注释明示）。
**Step 4: 验证** — PASS（超时/取消/一次性/deadline 默认值）。**Step 5: Commit** — `feat: IWaitable 等待原语（deadline/超时/取消）`

---

### Task 4: SourceGen — BehaviorInfoGenerator

**Files:**
- Create: `Queen.Generator/Queen.Generator.csproj`（`IsRoslynComponent`、`OutputItemType=Analyzer`）、`BehaviorInfoGenerator.cs`（`IIncrementalGenerator`）
- Create: `Queen.Core.Tests/Generator/BehaviorInfoGeneratorTests.cs`、`Generator/Golden/`

**Step 1: 写失败测试** — 四类：①golden 逐字节比对（`WalletInfo.generated.txt`）；②增量缓存（transform 计数器：首轮>0、同输入不变、改源重跑>首轮）；③产物编译（`GetDiagnostics()` 无错）；④候选检测（有特性进、无特性不进）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现（坑位全来自 2026-08-03）**
- driver 不可变：`driver = driver.RunGeneratorsAndUpdateCompilation(...)`，丢弃返回值 = NRE；不用 `AsSourceGenerator`、不用 options 重载。
- 测试传 `parseOptions: new CSharpParseOptions(LanguageVersion.Latest)` 防 re-parse 全 Modified。
- 生成内容：裸字段 → partial 属性 + `[Persistent]`/`[Projector]` 脏标记（`_dirty_` 位掩码）+ 快照（`_bak_` 备份 / `Rollback()` 恢复 / `Commit()` 清脏，9.1 语义）+ 序列化骨架占位（Phase 2 接 Truck）。partial 属性访问器定义侧不带 partial。
**Step 4: 验证** — 4/4 PASS；主工程 `dotnet build` 0 警告 0 错误（WalletInfo 迁 partial）。
**Step 5: Commit** — `feat: BehaviorInfo SourceGen（属性/脏标记/快照/序列化骨架 + 四类测试）`

---

### Task 5: Analyzer — QN1001/1002/1003

**Files:**
- Create: `Queen.Generator/Diagnostics.cs`（`QnAnalyzer : DiagnosticAnalyzer`）、`AnalyzerReleases.Shipped.md` + `Unshipped.md`（防 RS2008）

**Step 1: 写失败测试** — `QnAnalyzerTests.cs` 临时违规源断言：QN1001 `async` 方法（非 `[AllowAsync]` 类）报错；QN1002 前缀 `!`（`PrefixUnaryExpression.LogicalNotExpression`，不碰后缀 null 容忍 `x!`）；QN1003 `==`/`!=` 右侧 null/true/false 且左侧非常量 → 常量在左改写建议。
**Step 2: 运行确认失败** — FAIL。**Step 3: 实现** — 三规则（QN1002 只查前缀逻辑非）；QN1001 豁免类级 `[AllowAsync]`（按类名匹配，IO 抽象层用）。
**Step 4: 验证** — PASS；存量代码零违规（主工程+测试桩 build 通过）。
**Step 5: Commit** — `feat: QN Analyzer（禁 async/前缀!/常量在左）`

---

### Task 6: Job + JobContext + Radio 空壳

**Files:**
- Create: `Queen.Core/Scheduling/Job.cs`、`JobContext.cs`、`Queen.Core/Core/Radio.cs`

**Step 1: 写失败测试** — `JobContextTests.cs`：`Get<T>()` 无参恒取本 Actor store（同一实例断言）；`Get<R>(actorId)` O(1) 纯壳（零拉取、零引用计数、每次独立实例）；`MarkSave()` 置位。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现**
- `Job`：`DataStore? data`（`JobScheduler.Post` 经 `engine.service.GetStore(actorId)` 直连）、`IEnumerator body`、`JobSnapshot`（首写备份/Commit/Rollback 入口）、引用计数（`Get<T>` +1、Job 结束统一释放，Task 15 卸载保护）、`commitId`（Task 13 投影包序）。
- `JobContext`：静态路由直达 `current.data`——`Get<T>()`（命中即续；未命中自动创建 `WaitForLoad` 挂起）、`Get<R>(ulong)`（`new R()` + 注入 actorId/engine，`new()` 约束——测试壳需显式 public 构造）、`MarkSave()`。**无 `Load/LoadAll`**（#55）。
- `Radio`：抽象基类（actorId + `info`/`behavior` 两棵子树入口桩 `RadioInfoTree`/`RadioBehaviorTree` 空壳，SG 子壳后置）。
**Step 4: 验证** — PASS。**Step 5: Commit** — `feat: Job + JobContext + Radio 空壳（无参 Get 唯一归属 + Get<R> 壳）`

---

### Task 7: JobScheduler（就绪集合 + 预算 + 慢 Job + 串行 + 背压）

**Files:**
- Create: `Queen.Core/Scheduling/JobScheduler.cs`

**Step 1: 写失败测试** — `JobSchedulerTests.cs` 五项：①同 Actor 两 Job 严格串行；②就绪集合推进、空 Actor 不访问（无 O(N) 全扫）；③`starvationFrames` 预算、活跃 Actor 不独占；④慢 Job 超阈值记录+告警+下一 yield 点取消回滚（R21）；⑤`JOB_QUEUE_CAP` 满拒绝（busy 错误码）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — `Dictionary<ulong, Queue<Job>>` 就绪集合 + 每 Actor 预算（R4）；`Post(actorId, job, highValue=false)`；`Tick()` 按预算推进 `MoveNext` 到 yield 点返回；MoveNext 段墙钟计时（超预算→慢 Job 记录+下一 yield 点取消回滚）；单线程唯一执行上下文无锁；must 队列与 `MUST_BUDGET_PER_FRAME` 留字段骨架（Task 12 填充）。
**Step 4: 验证** — 5/5 PASS。**Step 5: Commit** — `feat: JobScheduler（就绪集合 + 预算 + 慢 Job + 串行 + 背压）`

---

### Task 8: Engine 主循环（Phase 1 子集 + MPSC IO 泵）

**Files:**
- Create: `Queen.Core/Core/IOPump.cs`；Modify: `Queen.Core/Core/Engine.cs`

**Step 1: 写失败测试** — `EngineTests.cs`：①空帧不忙转（`SleepUntilNextEvent` 语义）；②IO 结果经 MPSC 唤醒挂起 Job 并继续（WaitForLoad 桩完成）；③`yield return null` 的 Job 跨帧推进。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — 主循环（Phase 1 子集，顺序对齐 11 章）：`iopump.Drain()` → `timers.DrainTimers()` → `scheduler.Tick()` → `scheduler.ProcessActors()`（Commit/Rollback 结算）→ `projectors.Collect()` → `evict.EvictColdData()`。无固定帧率：空闲阻塞等待唤醒。`IOPump`：`ConcurrentQueue` + IO 线程 Post / 业务线程 Drain（MPSC 形态，真 IO 后置）。
**Step 4: 验证** — 3/3 PASS。**Step 5: Commit** — `feat: Engine 单线程主循环 + MPSC IO 泵`

---

### Task 9: Service + Behavior + 生命周期两件套

**Files:**
- Create: `Queen.Core/Core/Service.cs`、`Behavior.cs`

**Step 1: 写失败测试** — `ServiceLifecycleTests.cs`：①`AddActor(id)` 返回 DataStore、重复 ID 抛异常；②`GetStore` 未登记返回 null；③`AddInfo<T>()` 显式挂载；④`Active` 触发全部 Behavior `OnActive`、`Deact` 触发 `OnDeact`；⑤`RemoveActor` 在线先 Deact 再销毁 store；⑥API 面无 `Load/Unload`（编译期不存在，编译断言）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现**
- `Service`：`Dictionary<ulong, DataStore> stores` + behaviors 表 + active 集；`AddActor/GetStore/RemoveActor/Active/Deact/AddInfo<T>`；Service 创建事件（装配钩子，Task 11 用）。
- `Behavior`：纯逻辑基类——注入面仅 `actorId` + 受限门面（极薄：日志/时钟，不含 Service/容器，决策 #51）；仅 `OnActive`/`OnDeact` 两钩子（#55）；无 `OnLoad/OnUnload/OnTick`、无 `Behavior<T>` 泛型。
- 决策④：Actor 无 class，身份纯 `ulong` ID。
**Step 4: 验证** — 6/6 PASS。**Step 5: Commit** — `feat: Service 容器 + Behavior 基类 + 生命周期两件套`

---

### Task 10: TimerWheel（层级时间轮 + 系统家政钟 + Actor 声明钟）

**Files:**
- Create: `Queen.Core/Scheduling/TimerWheel.cs`

**Step 1: 写失败测试** — `TimerWheelTests.cs`：①到点投递定时 Job 给 JobScheduler（不直执行业务）；②集中到期 jitter 打散；③`OnActive` 挂钟 / `OnDeact` 摘钟——绝不为失活 Actor 跑定时器；④每 tick 投递上限（防到期风暴）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — 层级时间轮（O(1) 注册/扫描），Service 进程级设施，主循环 `DrainTimers()`。两半：**系统家政钟**（online 续期/幂等表 TTL/慢 Job 统计——Phase 1 留挂载点）+ **Actor 级声明钟**（`[TimerWheel(ID)]` 方法级特性：`public` 实例方法返回 `IEnumerator`、一方法一特性）。`timer_wheel` 表参数 Phase 1 默认值占位（Luban 后置），特性只声明、方法只执行。挂/摘由 `OnActive`/`OnDeact` 驱动（Task 11 装配后自动接线）；jitter phase 按 actorId 哈希打散；到期投 JobScheduler。
**Step 4: 验证** — 4/4 PASS。**Step 5: Commit** — `feat: TimerWheel（系统钟 + Actor 声明钟挂/摘 + jitter）`

---

### Task 11: BehaviorAssembler（#54 反射装配 + [TimerWheel] 收集）

**Files:**
- Create: `Queen.Core/Core/BehaviorAssembler.cs`

**Step 1: 写失败测试** — `BehaviorAssemblerTests.cs`：①业务零注册：Service 创建事件扫全部 `Behavior` 子类→开放委托工厂表，`AddActor` 自动实例化默认行为集（无 `AddBehavior`）；②启动期签名校验：`[TimerWheel]` 方法必须返回 `IEnumerator`，不符装配期报错；③运行期零反射（装配后工厂表 O(1)）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — Service 创建事件（Task 9 钩子）触发：Assembly 级一次扫 `Behavior` 子类 → `Func<Service, ulong, Behavior>` 开放委托工厂表 → `[TimerWheel]` 方法登记定时候选表；`AddActor` 自动实例化默认行为集 + 登记候选（激活后挂钟）；装配结果静态缓存、运行期零反射。
**Step 4: 验证** — 3/3 PASS。**Step 5: Commit** — `feat: BehaviorAssembler 自动装配（#54）+ [TimerWheel] 收集`

---

### Task 12: must 骨架（独立队列 + 配额 + 临界区 + 全成或全败 + R17 收尾）

**Files:**
- Create: `Queen.Core/Scheduling/Must.cs`、`MustContext.cs`；Modify: `JobScheduler.cs`

**Step 1: 写失败测试** — `MustTests.cs`：①同 Service 2 Actor 的 must 一次跑完双方字段、无中间态可见（禁 yield 保证）；②全成或全败：任一参与方条件不满足→快照回滚覆盖所有参与方、零副作用、返回原因码；③配额隔离：must 只占 must 配额、不挤 Job 预算；④FIFO 严格有序；⑤R17：成功才投影整包+尾随事件，全败不投影不事件。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现**
- `Must` 抽象基类：业务继承声明字段（如 `MustTransferGold`）；R9 重试参数（次数+超时，默认值可覆盖，Phase 1 仅声明）；R17 收尾回调（N 边投影整包 + 尾随事件）。
- `MustContext`：N 边字段专用访问入口（`GetInfo<T>(actorId)` 直连 DataStore 字段），禁 yield/禁 Get/禁循环、O(1) 临界区（#43 边界焊死）。
- `JobScheduler`：Service 级 must 独立队列 + FIFO + `MUST_BUDGET_PER_FRAME` 配额 + 与普通 Job 交替推进；快照回滚覆盖 N 边（复用 Task 4 SG 快照）。
- 参与方预加载重试（R9 完整版）与 must 内挂起（禁）留 Phase 2。
**Step 4: 验证** — 5/5 PASS。**Step 5: Commit** — `feat: must 骨架（独立队列 + 配额 + 全成或全败 + R17 收尾）`

---

### Task 13: ProjectorSystem 投影收集（Job 提交边界）

**Files:**
- Create: `Queen.Core/Projection/ProjectorSystem.cs`、`ProjectorPacket.cs`

**Step 1: 写失败测试** — `ProjectorTests.cs`：①一次 Job 提交 = 一个投影整包（含该提交所有 BehaviorInfo 的 `[Projector]` 字段 diff，钱包+背包同包，R8）；②`commitId` 按提交顺序递增；③回滚不收集（失败 Job 零投影）；④无投影字段不产生包；⑤包以提交边界原子产生（无部分包）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — `ProjectorPacket`：`actorId + commitId + entries(BehaviorInfo 类型 + 差异字段槽)` 整包结构。`ProjectorSystem`：挂在主循环 `Collect()`；Job 提交边界清 `_dirty_` 投影位 → 组装整包入发送队列（Phase 1 发送=丢入队列，网络后置）；回滚边界不收集。diff 来源 = Job 提交时写入动作记录的差异（8.1），非内存状态比对。
**Step 4: 验证** — 5/5 PASS。**Step 5: Commit** — `feat: ProjectorSystem（Job 提交边界整包 + commitId + 回滚不收集）`

---

### Task 14: Watchdog（心跳 500ms 判死 → dump 协程栈）

**Files:**
- Create: `Queen.Core/Watchdog/Watchdog.cs`

**Step 1: 写失败测试** — `WatchdogTests.cs`：心跳推进不判死；超 500ms 无心跳 → 触发 dump（断言 dump 回调被调 + 捕获当前协程栈）；恢复后继续心跳不重复 dump。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — 主循环外线程（或 IO 线程）监控 `lastPulse`，超过 500ms → 标记 + dump 当前执行中的 Job 协程栈（MoveNext 位置）；dump 后不崩溃、主循环恢复。挂在 Engine 心跳（每帧 Execute 开头 pulse）。
**Step 4: 验证** — 3/3 PASS。**Step 5: Commit** — `feat: Watchdog（500ms 判死 + 协程栈 dump）`

---

### Task 15: 冷数据卸载 EvictColdData（引用计数保护 + 内存桩闭环）

**Files:**
- Create: `Queen.Core/Core/EvictColdData.cs`（或并入主循环）

**Step 1: 写失败测试** — `EvictTests.cs`：①`lastAccessAt` 超阈值 + Job 引用计数为 0 才可卸载（双条件）；②引用计数 >0（挂起 Job 跨轮次引用）不可卸载——**竞态保护**；③卸载后再次 `Get<T>()` → 自动 WaitForLoad 懒加载（内存桩完成）→ Commit → diff 照常（冷热对投影无感）；④dirty 卸载前写回内存后端（Phase 1 等价 Truck 单条 flush）。
**Step 2: 运行确认失败** — FAIL。
**Step 3: 实现** — `EvictColdData`：扫 DataStore 按 `lastAccessAt` 老化（R7：读写都算访问、只存内存）；双条件卸载；卸载=从内存移除（dirty 先写回内存后端桩）+ Actor 壳常驻不受影响；再访问 `Get<T>` 未命中 → WaitForLoad → 内存后端恢复。引用计数由 Job 生命周期统一释放（Task 6 接线，Job 结束 ReleaseAll）。
**Step 4: 验证** — 4/4 PASS。**Step 5: Commit** — `feat: 冷数据卸载（双条件 + 引用计数保护 + 懒加载闭环）`

---

### Task 16: Phase 1 退出验收（Phase1ExitTests）

**Files:**
- Create: `Queen.Core.Tests/Phase1/Phase1ExitTests.cs`（集成场景，全链路线）

**Step 1: 写失败测试** — 端到端场景：`Service.AddActor` → `AddInfo` → 挂钟 → Job 修改字段 → Commit → 投影整包 → 闲置 → 卸载 → 再访问懒加载 → diff 照常；穿插失败 Job（回滚零投影）、慢 Job 告警、超时取消。
**Step 2: 运行确认失败** — 待全链可用后运行。
**Step 3: 实现** — 无需新组件，仅集成编排 + 断言 Phase 1 退出条件（architecture.md：Actor 串行性、yield-resume、慢 Job、异常隔离、看门狗 dump）。
**Step 4: 验证** — `dotnet test` 全绿（含前 15 Task 全部回归）。
**Step 5: Commit** — `test: Phase 1 退出验收（端到端场景）`

---

## 里程碑

| # | Task 集 | 验收 |
|---|---|---|
| M1 | T1-T3 | 工程骨架 + IWaitable 原语测试全绿 |
| M2 | T4-T6 | SG 四类测试 + Analyzer + Job/JobContext 数据入口形态 |
| M3 | T7-T9 | 调度器 + 主循环 + Service/Behavior 生命周期 |
| M4 | T10-T12 | 时间轮 + 自动装配 + must 骨架 |
| M5 | T13-T16 | 投影 + 看门狗 + 冷卸载 + 退出验收全绿 |

---

## 执行方式（待用户拍板）

对账顺序即本文件 Task 顺序；每个 Task 单独对账通过后按 TDD 实现。实现工程：
- `Queen.sln`：Queen.Core + Queen.Core.Tests + Queen.Generator（Generator 由 Tests 经 `OutputItemType=Analyzer` 双引用）
- 测试命令：`dotnet test`（单 Task 用 `--filter <ClassName>`）
- 提交粒度：每 Task 一个 commit（见各 Task Step 5）
