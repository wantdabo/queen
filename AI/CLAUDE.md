# CLAUDE.md

本文件为 Claude Code 在此仓库中工作时提供指导。

## 项目概述

Queen 是一个基于 .NET 8.0 的**游戏服务端核心库**，提供 ECS 基础设施和通用工具。

### 技术栈
MongoDB、Redis、TCP/UDP 网络、Luban 配置表、MessagePack 序列化

## 构建命令

```bash
dotnet build Queen/Queen.csproj
dotnet publish Queen/Queen.csproj -c Release
```

## 项目结构

```
Queen/           - 核心框架库
├── Core/        - 引擎和组件基类（Engine、Comp）
├── ECS/         - ECS 基础设施（Actor、Behavior、IBehaviorInfo）
├── Common/      - 通用工具（Logger、Eventor、Config、DBO）
├── Network/     - 网络通信
└── 3rd/         - 第三方库

Queen.Gateway/   - 网关进程，路由和负载均衡
Queen.Player/    - 玩家进程，可多实例水平扩展
Queen.Guild/     - 公会进程，可多实例水平扩展
```

### 进程模型
- 每个进程逻辑独立，物理上可共存或独立部署
- 跨进程通过 RPC 通信

### 线程模型
- **多进程 + 单线程**：每个 Engine 单线程，通过多进程水平扩展
- **异步不阻塞**：使用 `async/await`，代码连贯，不割裂上下文
- **Caller**：自定义 `SynchronizationContext`，保证 await 之后回到 Engine 线程
- **跨 Engine 通信**：通过消息/RPC，配合 `TaskCompletionSource` 实现异步等待

## 架构

### 组件系统
- `Engine` - 引擎基类，管理事件循环和内置组件
- `Comp` - 组件基类，支持组件树和生命周期（OnCreate/OnDestroy）
- 通过 `AddComp<T>()` 添加组件，`GetComp<T>()` 获取组件

### ECS 架构

| ECS 概念 | Queen 命名 | 说明 |
|---------|-----------|------|
| Entity | Actor | 实体，只存 ID |
| Component | IBehaviorInfo | 数据 |
| System | Behavior | 逻辑，继承 Comp |
| World | Shadow | 世界，管理 Actor 和 BehaviorInfo |

### 核心组件
- `Logger` - 异步日志，后台写盘
- `Eventor` - 事件系统，发布-订阅
- `Config` - Luban 配置加载器
- `DBO` - MongoDB 操作封装

---

## 编码风格

### 命名
- **属性**：小驼峰 `engine`、`logger`
- **方法**：大驼峰 `Create()`、`AddComp<T>()`
- **缩写词**：小写连写 `eventdict`、`dbhost`

### 代码风格
- `if (false == xxx)` 而非 `if (!xxx)`
- `if (null == obj)` 而非 `if (obj == null)`
- `T comp = new();` 而非 `T comp = new T();`
- 文件作用域命名空间 `namespace Queen.Core;`

### 注释
- 公开成员用 `<summary>` XML 注释
- 参数用 `<param>`，返回值用 `<returns>`

### 类设计
- 继承 `Comp` 基类
- 重写 `OnCreate()` / `OnDestroy()`
