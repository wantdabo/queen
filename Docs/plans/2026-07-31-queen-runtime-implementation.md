# Queen Runtime Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 从零实现 Queen 新架构的单线程 Actor 运行时、持久化数据模型、投影同步、跨 Service 异步通信，并为关键业务提供可恢复的分布式事务能力。

**Architecture:** 以单进程单线程协程调度为核心，一个 Actor 的状态只能由该 Actor 的 Job 修改。普通跨 Actor 写操作使用 at-least-once 异步消息；只有关键业务进入 `TransactionCoordinator`，通过 `Prepare → CommitDecision/AbortDecision → Confirm/Cancel` 完成统一决议。MongoDB 是持久化真相，投影使用版本号和全量快照恢复。

**Tech Stack:** .NET 8、C#、MessagePack-CSharp、MongoDB Driver、现有 Queen 项目结构；测试采用 xUnit、Microsoft.NET.Test.Sdk 和集成测试专用 MongoDB Replica Set。

---

## 可行性结论

| 范围 | 判断 | 依据 |
|---|---|---|
| 单线程 Actor、Job、协程调度 | 高 | 不依赖并发锁，边界清晰，可用纯内存测试验证 |
| `BehaviorInfo`、Source Generator、脏标记 | 高 | C# Roslyn Generator 和现有 MessagePack 依赖可支撑 |
| MongoDB 条件写与版本恢复 | 高 | MongoDB Driver 已存在，需补版本条件和失败测试 |
| Projector 增量同步 | 高 | 协议可版本化，快照兜底即可控制复杂度 |
| 跨 Service 异步消息 | 中高 | 主要风险是重试、去重、Redirect、死信语义 |
| Gateway/Router/迁移 | 中高 | 依赖所有权租约、fencing token 和重连状态机 |
| `TransactionCoordinator` 强事务 | 中 | 协议可实现，但需要持久化决议、参与者恢复和死信介入 |
| 零 GC、超高 CCU | 未定 | 必须以真实业务模型压测，不能从架构文字推导 |

第一阶段不要求实现全部目标。必须先证明单线程运行时和数据模型，再扩展网络、迁移和分布式事务。

## 前置约束

- `Docs/architecture.md` 是目标设计；当前业务代码只作为仓库结构和依赖参考。
- 先新增自动化测试项目，再实现核心模块；仓库当前没有可识别的测试项目。
- 业务层不使用 `async/await`；IO 通过可挂起的运行时适配器接入。
- 不引入 WAL；第一版依赖 MongoDB 持久化和允许的小窗口丢失语义。
- 不在第一阶段实现 Gateway、Router、Actor 迁移和分布式事务。

## Phase 0：测试与构建基线

**Files:**
- Create: `Queen.Tests/Queen.Tests.csproj`
- Create: `Queen.Tests/Runtime/RuntimeSmokeTests.cs`
- Modify: `Queen.sln`
- Check: `Queen/Queen.csproj`

**Steps:**
1. 创建 `Queen.Tests`，引用核心项目和测试 SDK。
2. 添加最小测试，验证测试发现和执行链路。
3. 运行 `dotnet test Queen.Tests/Queen.Tests.csproj`，确认测试失败/通过结果可见。
4. 为测试定义 fake clock、fake persistence、fake transport，禁止单元测试依赖真实网络。

**Exit condition:** `dotnet build Queen.sln` 和 `dotnet test Queen.Tests` 可重复执行；测试项目能独立运行。

## Phase 1：单进程核心运行时

**Files:**
- Create/Modify: `Queen/Core/Engine.cs`, `Queen/Core/Actor.cs`, `Queen/Core/Job.cs`
- Create: `Queen/Core/ActorState.cs`, `Queen/Core/JobContext.cs`
- Create: `Queen/Common/Scheduling/CoroutineScheduler.cs`
- Test: `Queen.Tests/Runtime/*`

**Steps:**
1. 写失败测试：同一 Actor 的两个 Job 不会交错修改同一临界区。
2. 实现 Actor mailbox、Job 状态和单线程就绪队列。
3. 写失败测试：`yield` 后 Job 可恢复，异常、取消、超时都会结束 Job。
4. 实现协程等待对象和取消传播。
5. 写失败测试：预算耗尽时长 Job 让出执行权，其他 Actor 不被饿死。
6. 实现公平调度、慢 Job 统计和异常隔离。
7. 增加重入、重复完成、Actor 销毁期间消息处理测试。

**Exit condition:** 串行性、恢复、取消、超时、异常隔离和公平调度测试通过。

## Phase 2：BehaviorInfo、DataStore 和持久化

**Files:**
- Create: `Queen/Core/Behavior.cs`, `Queen/Core/BehaviorInfo.cs`, `Queen/Core/DataStore.cs`
- Create: `Queen/Generated/` 下的 Source Generator 相关项目或生成入口
- Create/Modify: `Queen/Common/DB/*`
- Test: `Queen.Tests/Data/*`

**Steps:**
1. 定义 `[Persistent]`、`[Projector]`、字段版本和数据 Schema 约定。
2. 写失败测试：首次写入生成字段快照，Job 失败可恢复，成功后丢弃快照。
3. 实现 DataStore 懒加载，明确加载期间 Job 的挂起语义。
4. 写失败测试：MongoDB 条件写拒绝旧版本覆盖新版本。
5. 实现 MongoDB 文档整体写入、版本条件和重启加载。
6. 写失败测试：容器 Add/Replace/Remove 后脏状态与持久化序列化正确。
7. 实现 `GBLList/GBLDict` 和温度管理的最小可用版本。

**Exit condition:** 随机修改、Job 失败恢复、懒加载、重启加载和条件写冲突测试通过。

## Phase 3：Projector 与客户端同步

**Files:**
- Create: `Queen/Projection/ProjectorSystem.cs`
- Create: `Queen/Projection/ProjectorPacket.cs`
- Create/Modify: `Queen.Protocols/*`
- Test: `Queen.Tests/Projection/*`, `Queen.Protocols.Tests/*`

**Steps:**
1. 定义快照、增量包、`projectionVersion` 和字段/容器差异格式。
2. 写失败测试：随机增量序列可重建与服务端相同状态。
3. 实现 Projector 帧末收集和池化包。
4. 写失败测试：版本断裂触发全量快照，而不是继续应用增量。
5. 实现断线重连、快照确认和待发送投影重建。
6. 添加协议序列化、反序列化和协议号稳定性测试。

**Exit condition:** 客户端状态可由快照加任意合法增量序列恢复；版本断裂和重连可回到全量快照。

## Phase 4：单机多 Service 异步通信

**Files:**
- Create: `Queen/Network/Messaging/*`
- Create: `Queen/Network/Rpc/*`
- Modify: `Queen.Protocols/Common/*`
- Test: `Queen.Tests/Network/*`

**Steps:**
1. 定义 `messageId`、`requestId`、发送方序号、服务版本和错误码。
2. 写失败测试：重复消息只执行一次，乱序消息按协议处理，超时不会误判为未执行。
3. 实现 at-least-once 投递、inbox 去重、有限重试和死信。
4. 写失败测试：Redirect、服务重启、目标 Actor 迁移期间请求可恢复。
5. 实现异步 `Call`/事件回执；`Accepted` 不代表业务完成。
6. 增加跨 Actor 只能发送消息、不能同步修改目标状态的 API 约束。

**Exit condition:** 重复包、超时、重启、Redirect、死信和异步完成回执测试通过。

## Phase 5：Gateway、Router 与 Actor 所有权迁移

**Files:**
- Create: `Queen/Network/Gateway/*`
- Create: `Queen/Network/Router/*`
- Create: `Queen/Core/Migration/*`
- Test: `Queen.Tests/Migration/*`

**Steps:**
1. 定义 Actor 路由版本、lease、fencing token 和 session/resumeToken。
2. 写失败测试：旧所有者在 fencing 后不能继续写入。
3. 实现 Freeze → Flush → Transfer → Activate 状态机。
4. 写失败测试：迁移中请求、断线重连和迁移失败恢复不丢消息。
5. 实现连接池、多路复用和路由缓存失效。

**Exit condition:** 单主所有权、迁移失败恢复、重连和路由缓存失效测试通过。

## Phase 6：TransactionCoordinator 与运维闭环

**Files:**
- Create: `Queen/Transactions/TransactionCoordinator.cs`
- Create: `Queen/Transactions/TransactionRecord.cs`
- Create: `Queen/Transactions/ParticipantRecord.cs`
- Create: `Queen/Transactions/TransactionRecovery.cs`
- Create: `Queen/Operations/DeadLetter/*`
- Test: `Queen.Tests/Transactions/*`

**Steps:**
1. 定义事务状态：`Created`、`Preparing`、`Prepared`、`Committing`、`Committed`、`Aborting`、`Aborted`、`DeadLetter`。
2. 写失败测试：任一 Prepare 失败会产生持久化 `AbortDecision`。
3. 实现 Prepare 冻结和参与者幂等记录。
4. 写失败测试：CommitDecision 写入后，协调者重启只能继续 Confirm，不能改为 Abort。
5. 实现 Confirm/Cancel 重试、恢复扫描和参与者重启恢复。
6. 写失败测试：部分 Confirm 时客户端仍只看到 `Pending`，最终投影只在全体 Confirm 后产生。
7. 实现超时、重试耗尽、死信和人工介入接口。
8. 使用 MongoDB Replica Set 做事务记录和恢复集成测试。

**Exit condition:** 重启、重复包、部分 Confirm、协调者故障、参与者离线、超时和死信测试通过。

## Phase 7：性能、可观测性和发布验证

**Files:**
- Create: `Queen.Benchmarks/*`
- Modify: `Queen/Common/Logging/*`, `Queen/Operations/*`
- Test: `Queen.Tests/Performance/*`
- Check: `Docs/architecture.md`

**Steps:**
1. 建立 Actor 调度、投影收集、序列化和 RPC 的基准场景。
2. 压测单进程不同 Actor 数、Job 长度和消息突发量。
3. 测量 Queen 自有热路径分配、整进程分配、P99 延迟、Gen2 间隔和吞吐。
4. 增加 requestId/transactionId 链路日志、指标和慢 Job 告警。
5. 执行 MongoDB 备份恢复、灰度和回滚演练。

**Exit condition:** 性能目标以真实数据记录；所有架构约束都有自动化测试或可观测指标，不把“零 GC/超高 CCU”当作未经验证的保证。

## 主要风险与控制

- **协程 Job 长时间不让出**：Analyzer/运行时预算告警；长计算迁移到专用 Service。
- **旧实现污染新架构**：新运行时先建独立命名空间和测试，禁止边实现边兼容旧 Actor 语义。
- **MongoDB 条件写不足**：所有持久化写必须带 `actorId + version` 条件；集成测试覆盖并发旧写。
- **消息重复造成重复业务**：业务命令必须带 `messageId/requestId`，inbox 记录与业务结果关联。
- **协调者双主**：采用 lease/fencing token；事务决议使用唯一条件写，旧协调者不能继续发有效决议。
- **部分 Confirm 对外泄漏**：事务相关状态统一经过事务视图；最终投影等待全部参与者完成。
- **性能目标失真**：先建立基准和业务负载模型，再决定是否需要池化、批处理或多进程扩容。

## 开工顺序

严格按 `Phase 0 → Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7` 推进。每个 Phase 以测试和退出条件验收后再进入下一阶段；Phase 6 的强事务不是核心运行时的前置依赖。
