# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在此仓库中工作时提供指导。

## 项目概述

Queen 是一个基于 .NET 8.0 的游戏服务端框架，采用组件-引擎架构设计。支持 MongoDB 数据库、Redis 缓存、TCP/UDP 网络通信，使用 Luban 进行配置表管理，MessagePack 进行序列化。

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
Queen/           - 核心框架库
├── Core/        - 引擎和组件基类
├── Common/      - 通用功能（日志、事件、配置、数据库）
├── 3rd/         - 第三方库（LubanLib）
└── Res/         - 资源文件（配置二进制）

Config/          - 配置管理
├── Datas/       - Excel 配置源文件
├── Cfg/         - 生成的代码和数据
├── Commands/    - 生成脚本
└── Tools/       - Luban 和 protoc 工具

Commands/        - 协议生成脚本
```

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
