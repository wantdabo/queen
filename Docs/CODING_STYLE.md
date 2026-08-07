# Queen 编码规范

> 2026-08-02 | 参照 Goblin 风格（`goblin/.codebuddy/rules/rules.mdc` + `goblin/Docs/CODING_STYLE.md`），按 Queen 架构（单线程协程、Source Generator、Job、Radio 壳）适配。
>
> **本文档是唯一编码权威。** 与 `Docs/architecture.md` 冲突时，架构文档优先，但代码示例以架构文档中的示例为准。

---

## 1. 命名

### 1.1 属性与字段：一律小写

所有属性与字段——公开、私有、protected——**一律 camelCase 全小写**。不 `_` 前缀，不 PascalCase。

```csharp
// ✅
public int gold;
public bool destroyed { get; private set; }
public Engine engine { get; set; }
private readonly int ethreadId;
protected Stage stage { get; private set; }

// ❌
public int Gold;
private int _gold;
```

### 1.2 BehaviorInfo partial 属性 + SG 生成实现

`BehaviorInfo` 是纯数据 Component，**手写 partial class 声明 partial 属性**（C# 14 partial 属性方法，小写），Source Generator 生成属性实现（backing 字段 + 写拦截）、序列化、脏标记和快照代码。业务代码以属性名访问（编译后即生成的带脏标记属性），**不得手写非 partial 属性、不得绕过生成写入口**：

```csharp
[Persistent, Projector]
public partial class WalletInfo : BehaviorInfo
{
    [Persistent]
    public partial int gold { get; set; }

    public partial int money { get; set; }

    [Projector]
    public partial int total { get; set; }
}
```

> SG 生成的快照/脏标记字段名为 `_bak_`/`_dirty_`（architecture.md 9.1 定义），属生成物内部命名，不受 1.1「不 `_` 前缀」约束。

字段语义（与 6.3 一致）：

| 声明 | 语义 |
|---|---|
| `[Persistent, Projector]` | 写盘并推送 |
| `[Persistent]` | 只写盘，适合敏感字段 |
| `[Projector]` | 只推送，适合派生或运行时字段 |
| `[Fetchable]` | 允许其他 Actor 通过 Radio 壳只读访问（生成 `radio.info.*` 只读壳属性） |
| 无声明 | 内部状态 |

### 1.3 类/方法 PascalCase，钩子 `On` 前缀

```csharp
public sealed class WalletBehavior : Behavior { ... }
protected override void OnActive() { ... }
public void Run() { ... }
```

生命周期钩子固定 `On` 前缀：Actor 两件套 `OnActive` / `OnDeact`（无 `OnLoad`/`OnUnload`——数据进出内存是框架内部事务，业务无感）。

### 1.4 常量 SCREAMING_SNAKE_CASE

```csharp
// ✅
public const int MAX_BATCH_SIZE = 100;
public const float DEFAULT_TIMEOUT = 3f;

// ❌
public const int MaxBatchSize = 100;
public const int maxBatchSize = 100;
```

### 1.5 动词缩写

短名优先：

| 缩写 | 全称 | 示例 |
|------|------|------|
| `Rmv` | Remove | `RmvActor` |
| `Gen` | Generate | `GenId` |
| `Seek` | Find/Lookup | `SeekBehavior` |
| `Tell` | Dispatch/Send | `Tell<T>(e)` |

### 1.6 协议消息

网络消息实现 `INetMessage`，命名 `C2S`/`S2C` 前缀 + 业务名 + `Msg` 后缀：

```csharp
public class C2SLoginMsg : INetMessage { ... }
public class S2CLoginMsg : INetMessage { ... }
```

### 1.7 泛型约束

- `Get<T>()` 无参恒取本 Actor 的 Info，约束 `where T : BehaviorInfo`；返回 IWaitable（`yield return ctx.Get<T>()`），命中即续、未命中自动挂起加载——无显式 `Load<T>()`/`LoadAll()`。
- `Get<R>(actorId)` 取其他 Actor 的 Radio 壳，约束 `where R : Radio`。

---

## 2. 协程与 Job（Queen 特有，强制）

- Job 方法返回 `IEnumerator`，挂起点用 `yield return`：
  - `yield return Get<T>()`——取本 Actor 的 Info（命中即续、未命中自动挂起加载；无显式 `Load<T>()`/`LoadAll()`）；
  - `yield return radio.info.bag`——子壳获取器（按需拉取）；
  - `yield return radio.behavior.bag.Give(...)`——内部 RPC（`[Remote]`）；
  - `yield return WaitForXxx(...)`——其他 IWaitable 等待器。
- 提前结束用 `yield break`。
- **禁 `async`/`await`（QN1001 保留）**，所有挂起必须走调度器可见的 IWaitable，不允许同步代码中隐式阻塞或隐藏 IO。
- 显式循环 N 次（for/while）必须 `yield return null`（Analyzer 强制），禁止无 yield 的长循环。
- 长同步运算不塞进 Job，走专用 Service。
- 一个 Job 只直接写自己的 Actor；跨 Actor 写通过 Radio 壳 `[Remote]` 方法（目标 Actor 自己执行）。

```csharp
[RpcMethod]
public IEnumerator Spend(int cost)
{
    // 无参：恒取本 Actor 的 Info
    var info = yield return Get<PlayerBehaviorInfo>();
    if (info.gold < cost) yield break;

    info.gold -= cost;
    info.total = info.gold + info.money;
}
```

---

## 3. 条件判断：常量在前

`null` 和 `false` 永远放左边，不用 `!` 取反。

```csharp
// ✅
if (null == comps) return;
if (false == eventDict.TryGetValue(typeof(T), out var funcs)) return;

// ❌
if (comps == null) return;
if (!eventDict.TryGetValue(...))
```

---

## 4. 简短卫语句单行

`if + return/break/continue` 体量小时合并一行：

```csharp
// ✅
if (null == info) return;
if (false == Relation.Exists(id, targetId)) yield break;
if (info.gold < cost) yield break;

// ❌
if (null == info)
{
    return;
}
```

---

## 5. 注释规范（极其重要）

- **注释独占一行，禁止行尾注释**。
- `/// <summary>` XML 文档注释，中文。
- 行内注释 `//` 中文。
- 不写英文注释。

```csharp
// ✅
/// <summary>
/// 执行移动计算
/// </summary>
// 检查是否在线
if (false == online) return;

// ❌
public void Tick() {  // 每帧调用            ← 行尾注释，禁止！
// Check if actor exists                     ← 英文注释，禁止！
```

---

## 6. 缩进与编码

- 4 空格缩进（不用 Tab）。
- 文件级命名空间（`namespace Queen.Core;` 末尾分号）。
- **LF 行尾、UTF-8 BOM（2026-08-03 用户裁决：统一 LF，原 CRLF 作废）。**
- 不要在代码行后加注释。

---

## 7. 分配意识（2026-08-03 用户裁决：池化后置）

- GC 是软目标而非硬指标（architecture.md 12.2），不设字节级硬预算；骨架阶段不建池化设施（无 ObjectCache）。
- 后续在敏感热路径（Job 对象 / 等待器 / 投影包 / 快照上下文）再提供池化能力，框架与业务共用，业务也可自行使用。
- 投影打包、容器差异和临时集合在池化启用后使用对象池或 `ArrayPool`。
- 容器元素 struct/不可变，改 = 替换式（与池化解耦，独立成立）。
- 帧末延迟删除（`rmvactorset` / `rmvbehaviors`），不在遍历中删除。
- 热路径禁止 LINQ 装箱与隐式分配。

---

## 8. 严禁事项（检查清单）

每次修改代码前对照此清单：

| ❌ 禁止 | ✅ 正确 |
|---|---|
| 属性/字段 PascalCase（`Gold`） | `gold` |
| 属性/字段 `_` 前缀（`_gold`） | `gold` |
| `if (x == null)` | `if (null == x)` |
| `if (!condition)` | `if (false == condition)` |
| 行尾注释 `foo; // 注释` | 注释独占上一行 |
| 英文注释 | 中文注释 |
| `async`/`await` | `IEnumerator` + `yield return` |
| 无 yield 的长循环 | 循环体必须 `yield return null` |
| BehaviorInfo 手写属性/绕生成写入口 | 只声明 public 裸字段 |
| 无界 `A → B → A` 同步等待循环 | 经 requestId/深度限制检测 |
| emoji | 无 |
| Tab 缩进 | 4 空格 |
| `if + return` 拆多行 | 合并一行 |

---

## 9. 架构速查

- 单线程 + 协程交替（IEnumerator），绝对无锁；多核靠多进程。
- Job 是本地原子边界：成功才发布内部事件、回复和投影；失败快照回滚。
- 跨 Actor 读写统一走 Radio 壳 `Get<R>(actorId)`：`radio.info.*` 只读快照、`radio.behavior.*` 方法即 Call。
- `must`：同 Service 多 Actor 批原子，临界区内禁 `yield`/禁 DataStore 读/禁循环/禁消息投递，O(1) 完成，全成或全败。
- 数据归属：谁的数据放谁身上；跨 Service 只传通知不传真相（10.3 SOP）。
- 冷热卸载单元 = BehaviorInfo；判定 = `lastAccessAt` 超阈值 **且** Job 引用计数为零。
- Source Generator：`partial class` + 标记特性生成属性/序列化/脏标记/快照/Radio 壳；生成物 `SG` 前缀。
- 生命周期：Actor 两件套 `OnActive`/`OnDeact`（无 `OnLoad`/`OnUnload`）；Info 无方法；Behavior 只声明 `[RpcMethod]`/`[Remote]` 协程方法。
- 详细架构见 [architecture.md](architecture.md)。
