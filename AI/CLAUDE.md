# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在此仓库中工作时提供指导。

## 项目概述

Queen 是一个基于 .NET 8.0 的**游戏服务端核心库（Lib）**，提供 ECS 基础设施和通用工具，供上层业务项目（如 Queen.Server）使用。

### 定位
- **Queen** - 核心库，实现 ECS 底层、通用工具、网络、数据库等基础设施
- **Queen.Server** - 业务项目，基于 Queen 实现具体游戏逻辑（Player、Guild 等）

### 技术栈
MongoDB 数据库、Redis 缓存、TCP/UDP 网络通信、Luban 配置表、MessagePack 序列化

## 构建命令

```bash
# 构建项目
dotnet build Queen/Queen.csproj

# 发布 Release 版本
dotnet publish Queen/Queen.csproj -c Release
```

## 代码生成命令

### 配置表生成（Luban）
```bash
# Windows - 生成配置代码和二进制数据
Config/Commands/gen.bat

# 手动执行
cd Config
dotnet Tools/Luban/Luban.dll -t all -c cs-bin -d bin --conf luban.conf
```
- 输出 C# 代码到 `Config/Cfg/CS/`
- 输出二进制数据到 `Config/Cfg/Bytes/`
- 配置源文件在 `Config/Datas/*.xlsx`

### 协议生成（MessagePack）
```bash
# Windows
Commands/gen_resolver.bat
Commands/proto.bat
```

## 架构

### 组件系统
- `Engine` - 引擎基类，管理事件循环（1ms tick）和内置组件（Logger、Eventor、Random）
- `Comp` - 组件基类，支持组件树结构和生命周期管理（OnCreate/OnDestroy）
- 通过 `AddComp<T>()` 添加组件，`GetComp<T>()` 获取组件

### 核心组件（Queen/Common/）
- `Logger` - 异步日志系统，后台线程写盘，按日期轮转文件
- `Eventor` - 事件系统，发布-订阅模式，类型安全
- `Random` - 随机数生成器，支持整数和浮点数范围
- `Config` - Luban 配置加载器，通过 `Tables` 类访问配置
- `DBO` - MongoDB 数据库操作封装，支持 CRUD 和批量操作

### 数据库值类型（Queen/Common/DB/）
- `DBValue<T>` - 数据库值基类
- `DBRoleValue` - 角色相关数据
- `DBDataValue` - 通用数据

## 项目结构

```
Queen/              - 核心框架库（Lib）
├── Core/           - 引擎和组件基类（Engine、Comp）
├── ECS/            - ECS 基础设施（Actor、Behavior、BehaviorInfo）
├── Common/         - 通用工具（Logger、Eventor、Config、DBO）
├── Network/        - 网络通信
├── 3rd/            - 第三方库（LubanLib）
└── Res/            - 资源文件

Queen.Player/       - Player 进程（独立部署）
├── Logic/          - Player 业务逻辑
└── Behaviors/      - Player 的 Behavior 实现

Queen.Guild/        - Guild 进程（独立部署）
├── Logic/          - Guild 业务逻辑
└── Behaviors/      - Guild 的 Behavior 实现

Queen.Gateway/      - 网关进程（独立部署）

Queen.Rank/         - 排行榜进程（独立部署）

Queen.Chat/         - 聊天进程（独立部署）

Queen.Match/        - 匹配进程（独立部署）

Config/             - 配置管理
├── Datas/          - Excel 配置源文件
├── Cfg/            - 生成的代码和数据
├── Commands/       - 生成脚本
└── Tools/          - Luban 和 protoc 工具

Commands/           - 协议生成脚本
```

### 进程独立部署
- **Queen.Player** - Player 进程，可多实例水平扩展
- **Queen.Guild** - Guild 进程，可多实例水平扩展
- **Queen.Gateway** - 网关进程，负责路由和负载均衡
- **Queen.Rank** - 排行榜进程，弱一致，异步更新
- **Queen.Chat** - 聊天进程，消息流模型
- **Queen.Match** - 匹配进程，短生命周期

每个进程逻辑上独立，物理上可共存或独立部署，通过 RPC 通信。

## 依赖项

| 包 | 版本 | 用途 |
|---|---|---|
| MongoDB.Driver | 2.26.0 | 数据库 |
| StackExchange.Redis | 2.8.0 | 缓存 |
| LiteNetLib | 1.2.0 | UDP 网络 |
| TouchSocket | 3.0.6 | TCP/WebSocket |
| Newtonsoft.Json | 13.0.3 | JSON 序列化 |

## 配置表定义

配置表使用 Luban 工具，在 `Config/Datas/__tables__.xlsx` 中定义表结构，数据文件如 `ItemData.xlsx`。生成后通过 `Config.location` 访问配置数据。

---

## Queen 编码风格

### 命名规范
- **属性**：小驼峰 `camelCase`，如 `engine`、`logger`、`eventor`、`dbhost`
- **私有属性**：小驼峰 + `{ get; set; }`，如 `private Comp parent { get; set; }`
- **方法**：大驼峰 `PascalCase`，如 `Create()`、`Destroy()`、`AddComp<T>()`
- **类型参数**：单字母大写 `T`
- **缩写词拼接**：小写连写，如 `compdict`、`eventdict`、`dbhost`、`dbpwd`

### 代码风格
- **条件判断**：`if (false == xxx)` 而不是 `if (!xxx)`
- **null 判断**：`if (null == comps)` 而不是 `if (comps == null)`
- **延迟初始化**：`if (null == comps) comps = new();`
- **new() 简写**：`T comp = new();` 而不是 `T comp = new T();`
- **命名空间**：文件作用域 `namespace Queen.Core;`

### 注释规范
- 所有公开成员都有 `<summary>` XML 注释
- 方法参数使用 `<param>` 注释
- 返回值使用 `<returns>` 注释
- 泛型参数使用 `<typeparam>` 注释

### 类设计模式
- 继承 `Comp` 基类
- 重写 `OnCreate()` / `OnDestroy()` 生命周期
- 使用 `{ get; set; }` 或 `{ get; private set; }` 属性

### 代码示例
```csharp
using Queen.Core;

namespace Queen.Common;

/// <summary>
/// 事件接口
/// </summary>
public interface IEvent
{
}

/// <summary>
/// 事件订阅派发者
/// </summary>
public class Eventor : Comp
{
    /// <summary>
    /// 事件的集合
    /// </summary>
    private Dictionary<Type, List<Delegate>> eventdict { get; set; }

    protected override void OnCreate()
    {
        base.OnCreate();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (null == eventdict) return;
        eventdict.Clear();
    }

    /// <summary>
    /// 注册事件监听
    /// </summary>
    /// <typeparam name="T">事件的结构体</typeparam>
    /// <param name="func">事件的回调</param>
    public void Listen<T>(Action<T> func) where T : IEvent
    {
        if (null == eventdict) eventdict = new();

        if (false == eventdict.TryGetValue(typeof(T), out var funcs))
        {
            funcs = new List<Delegate>();
            eventdict.Add(typeof(T), funcs);
        }
        if (funcs.Contains(func)) return;

        funcs.Add(func);
    }

    /// <summary>
    /// 派发事件
    /// </summary>
    /// <typeparam name="T">事件的结构体</typeparam>
    /// <param name="e">事件的参数</param>
    public void Tell<T>(T e = default) where T : IEvent
    {
        if (null == eventdict) return;
        if (false == eventdict.TryGetValue(typeof(T), out var funcs)) return;
        for (int i = funcs.Count - 1; i >= 0; i--) funcs[i].DynamicInvoke(e);
    }
}
```

---

## 服务器 ECS 架构设计

### 设计目标
- **水平扩展优先** - 通过多进程、多实例解决规模问题，不追求单机极致性能
- **确定性**（Deterministic）- 顺序确定、可预测、可回放
- **逻辑清晰、可测试** - 数据与逻辑分离
- **开发效率** - 不过度关注 GC 和微观性能优化，允许使用引用类型

### 核心原则
- 服务器 ECS ≠ Unity DOTS，不需要 Jobs/Burst/NativeContainer
- **不抠 GC、不抠微观性能热点**，规模问题靠水平扩展解决
- 要的是：**数据驱动 + 顺序确定 + 易于扩展**

### 三层 ECS 架构（Queen 命名）

| ECS 标准概念 | Queen 命名 | 说明 |
|-------------|-----------|------|
| Entity | Actor | 实体，通用的，只存身份 ID |
| Component | BehaviorInfo | 数据，可以是 struct 或 class |
| System | Behavior | 逻辑，继承 Comp |
| World | Engine | 世界，一个 Engine 管理所有 Actor 和 BehaviorInfo |

### 设计理念
- **Engine = World**，一个 Engine 管理所有 Actor 和 BehaviorInfo
- **Actor 是通用的**，只存身份 ID，不需要 PlayerActor/GuildActor 子类
- **Actor 是钥匙**，通过 Actor 去 Engine 获取对应的 BehaviorInfo
- **Behavior 是逻辑**，操作 BehaviorInfo 数据

### Queen/ECS/ 需要实现的基础类

#### Actor（实体）
```csharp
namespace Queen.ECS;

/// <summary>
/// 实体（通用）
/// </summary>
public class Actor
{
    /// <summary>
    /// 实体 ID
    /// </summary>
    public string id { get; private set; }

    /// <summary>
    /// 创建实体
    /// </summary>
    /// <param name="id">实体 ID</param>
    public Actor(string id)
    {
        this.id = id;
    }
}
```

#### IBehaviorInfo（数据接口）
```csharp
namespace Queen.ECS;

/// <summary>
/// 行为数据接口
/// </summary>
public interface IBehaviorInfo
{
}

/// <summary>
/// 背包信息
/// </summary>
public class InventoryInfo : IBehaviorInfo
{
    /// <summary>
    /// 物品列表
    /// </summary>
    public List<Item> items { get; set; } = new();

    /// <summary>
    /// 容量
    /// </summary>
    public int capacity { get; set; }
}

/// <summary>
/// 任务信息
/// </summary>
public class QuestInfo : IBehaviorInfo
{
    /// <summary>
    /// 任务列表
    /// </summary>
    public List<Quest> quests { get; set; } = new();
}
```

#### Behavior（行为基类）
```csharp
using Queen.Core;

namespace Queen.ECS;

/// <summary>
/// 行为基类
/// </summary>
public abstract class Behavior : Comp
{
    protected override void OnCreate()
    {
        base.OnCreate();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}

/// <summary>
/// 行为基类
/// </summary>
/// <typeparam name="T">引擎类型</typeparam>
public abstract class Behavior<T> : Behavior where T : Engine, new()
{
    /// <summary>
    /// 引擎
    /// </summary>
    public new T engine { get { return base.engine as T; } }
}
```

#### 使用示例

**PlayerEngine（管理所有玩家）**
```csharp
using Queen.Core;
using Queen.ECS;

namespace Queen.Player;

/// <summary>
/// 玩家引擎
/// </summary>
public class PlayerEngine : Engine
{
    /// <summary>
    /// 玩家集合
    /// </summary>
    private Dictionary<string, Actor> actors { get; set; } = new();

    /// <summary>
    /// 背包数据
    /// </summary>
    private Dictionary<string, InventoryInfo> inventorys { get; set; } = new();

    /// <summary>
    /// 任务数据
    /// </summary>
    private Dictionary<string, QuestInfo> quests { get; set; } = new();

    /// <summary>
    /// 背包行为
    /// </summary>
    public InventoryBehavior inventory { get; private set; }

    /// <summary>
    /// 任务行为
    /// </summary>
    public QuestBehavior quest { get; private set; }

    public override bool execute => true;

    protected override void OnCreate()
    {
        base.OnCreate();

        inventory = AddComp<InventoryBehavior>();
        inventory.Create();

        quest = AddComp<QuestBehavior>();
        quest.Create();
    }

    /// <summary>
    /// 创建玩家
    /// </summary>
    /// <param name="playerid">玩家 ID</param>
    /// <returns>玩家</returns>
    public Actor CreateActor(string playerid)
    {
        var actor = new Actor(playerid);
        actors.Add(playerid, actor);
        inventorys.Add(playerid, new InventoryInfo());
        quests.Add(playerid, new QuestInfo());

        return actor;
    }

    /// <summary>
    /// 获取玩家
    /// </summary>
    /// <param name="playerid">玩家 ID</param>
    /// <returns>玩家</returns>
    public Actor GetActor(string playerid)
    {
        if (false == actors.TryGetValue(playerid, out var actor)) return null;
        return actor;
    }

    /// <summary>
    /// 获取背包数据
    /// </summary>
    /// <param name="actor">玩家</param>
    /// <returns>背包数据</returns>
    public InventoryInfo GetInventory(Actor actor)
    {
        if (false == inventorys.TryGetValue(actor.id, out var info)) return null;
        return info;
    }

    /// <summary>
    /// 获取任务数据
    /// </summary>
    /// <param name="actor">玩家</param>
    /// <returns>任务数据</returns>
    public QuestInfo GetQuest(Actor actor)
    {
        if (false == quests.TryGetValue(actor.id, out var info)) return null;
        return info;
    }

    /// <summary>
    /// 销毁玩家
    /// </summary>
    /// <param name="playerid">玩家 ID</param>
    public void DestroyActor(string playerid)
    {
        actors.Remove(playerid);
        inventorys.Remove(playerid);
        quests.Remove(playerid);
    }
}
```

**InventoryBehavior（背包行为）**
```csharp
using Queen.ECS;

namespace Queen.Player;

/// <summary>
/// 背包行为
/// </summary>
public class InventoryBehavior : Behavior<PlayerEngine>
{
    /// <summary>
    /// 添加物品
    /// </summary>
    /// <param name="actor">玩家</param>
    /// <param name="itemid">物品 ID</param>
    /// <param name="count">数量</param>
    public void AddItem(Actor actor, int itemid, int count)
    {
        var info = engine.GetInventory(actor);
        if (null == info) return;

        // 业务逻辑
    }
}
```

### BehaviorInfo 设计约束
- **可以是 struct 或 class**，允许引用类型
- **BehaviorInfo = 状态集合**，不是行为碎片
- 正确示例：`InventoryInfo`、`QuestInfo`、`CurrencyInfo`
- 错误示例：`AddItemInfo`、`QuestAcceptInfo`（这些应该是 Behavior）

---

## Queen.Player 架构设计

### 核心模型
- 每个 Player 是一个独立的 `World`
- **单线程执行**，玩家之间完全隔离
- 可动态分配到任意进程，支持负载均衡
- 所有 Behavior 在同一 World 内可安全相互访问

### 进程模型
```
Queen.Player 进程
├── World (玩家1)
├── World (玩家2)
├── World (玩家3)
```
**不是** One Big World（危险）

### World 内部结构
```
PlayerWorld
├── InventoryInfo   // 背包数据
├── QuestInfo       // 任务数据
├── ShopInfo        // 商城数据
├── CurrencyInfo    // 货币数据
├── CooldownInfo    // 冷却数据
```

### Behavior 定位
```
PlayerWorld
├── InventoryBehavior  // 只操作这个玩家的数据
├── QuestBehavior
├── ShopBehavior
```
- Behavior 不跨玩家、不做网络
- 接近 DDD + ECS 的混合体

---

## Queen.Guild 架构设计

### 同类系统（技术点一致）
- Guild（工会）、联盟、家族、战队
- 阵营、国家（SLG/MMO）
- 房间、对局（非战斗）
- 拍卖行、世界 Boss、副本实例

### 识别公式
满足以下条件 = Guild 类系统：
1. 多个玩家共享同一份状态
2. 不能随意拆分
3. 有管理/权限/资产

### 核心原则
- **一个 Guild = 一个 World**，单线程执行
- **绝对不能开机全量 load**（50W 工会全 load 是灾难）
- Guild 是"被访问时才存在的状态机"

### 三层模型（关键）
```
[ Persistent Storage ]  ← DB 持久化
        ↓
[ Meta / Index Layer ]  ← 常驻内存（只存 id、owner、status、version、shard）
        ↓
[ Runtime World Layer ] ← 按需创建的 World
```

### 加载策略
- **Lazy Load**：有请求才加载
- **热点常驻**：活跃 Guild 延长驻留时间
- **冷数据 Unload**：N 分钟无访问 → Unload
- **部分装载**：只加载当前操作需要的 BehaviorInfo

### Load 触发条件
- 有玩家访问（查看、捐献、管理）
- 系统事件（结算、活动）
- 后台操作（GM、数据修复）

### Unload 流程
```
1. Freeze World
2. Flush Dirty BehaviorInfo
3. Dispose World
```

### GuildWorld 结构
```
GuildWorld
├── MemberInfo       // 成员数据（必须）
├── AssetInfo        // 资产数据
├── ConfigInfo       // 配置数据

Behavior:
├── JoinBehavior
├── DonateBehavior
├── KickBehavior
```

---

## 进程部署策略

### 推荐架构
```
Queen.Gateway
├── Queen.Player (多实例)
├── Queen.Guild (多实例)
├── Queen.Rank
├── Queen.Chat
├── Queen.Match
```

### 部署原则
- **逻辑上独立，物理上可共存**
- Queen.Player 和 Queen.Guild 可以在同一机器（优化），也可以独立部署（高峰期）
- 代码完全不感知部署方式
- **所有跨 World 通信走 RPC**（即使同进程也当"远程"）

### 三条铁律
1. **一个 World = 一个串行执行上下文**（永远单线程）
2. **World 之间只通过消息/RPC 交互**（禁止直接引用 BehaviorInfo）
3. **路由以 WorldId 为准**（PlayerId/GuildId → Process）

### 动态迁移
- Player/Guild 的 World 可在任意进程出现，动态分配
- 迁移流程：Freeze → Flush State to DB → Update Locator → Load on Target → Unfreeze
- 迁移期间有短暂"冻结期"，返回 BUSY/RETRY

---

## 数据库分片（Shard）策略

### 分片原则
- 按 PlayerId/GuildId hash 到不同数据库实例
- 水平扩展的是"数据量"，不是单个 World 的并发能力

### 批量写策略
由于 Player/Guild 的 World 动态分布在不同进程，传统本地批量写失效。

**方案一：Shard Owner**
- 每个 shard 有唯一归属进程，负责 DB 批量写
- World flush 队列发送给 shard owner
- World 可自由迁移，写入归属 shard owner

**方案二：分布式 Flush 服务**
```
PlayerWorld (任意进程)
└─ enqueue flush queue (Redis/MQ) → shard dispatcher → DB batch write
```
- 按 shard 合并所有进程的写入
- 批量写入数据库

### 写入分类
- **强一致数据**（背包、任务、权限）→ 本地 flush / shard owner
- **弱一致数据**（统计、排行榜、日志）→ 聚合 flush，可延迟

---

## CCU 估算

### 单进程能力
- 单核 CPU：~20k req/s
- 单玩家平均 1 req/s → 单核支持 ~20k CCU
- 8 核进程（6 核可用）→ 保守 20k CCU/进程

### 横向扩展
| Queen.Player 进程数 | 保守 CCU | 理论 CCU |
|--------------------|---------|---------|
| 5                  | 10w     | 50w     |
| 10                 | 20w     | 100w    |
| 50                 | 100w    | 500w    |
| 100                | 200w    | 1000w   |

### 瓶颈点
- DB 写入吞吐 → 异步批量 + shard
- 网络带宽 → 心跳/同步优化
- 热点 Guild/Channel → 限流、缓存
- World 内存占用 → Lazy Load + 冷 World 回收

### 结论
此架构理论上可支撑**千万级 CCU**，属于大厂级 MMO/MOBA 服务器设计思路。
