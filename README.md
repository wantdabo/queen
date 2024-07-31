# Queen

这是基于 .NET 的多进程单线程执行的跨平台服务端。在这个高并发，高性能，分布式的时代，有时候，不需要那么高的性能，只需要一个简单，易用的服务器罢了

### 大致全貌
- ENet 套字节
- MessagePack 协议
- MongoDB 数据库
- Luban 配置

### <span id="catalog">目录</span>

- [1.快速开始](#qstart)
- [2.环境配置](#installenv)
    - [1.安装 .NET](#installenv.1)
    - [2.安装/配置 MongoDB](#installenv.2)
        - [1.下载安装 MongoDB](#installenv.2.1)
        - [2.数据库配置](#installenv.2.2)
- [3.网络协议](#netproto)
    - [1.定义协议](#netproto.1)
    - [2.生成协议](#netproto.2)
    - [3.使用协议](#netproto.3)
- [4.配置表]()
- [5.服务器配置](#servsettings)

### [项目结构](#projectdire)
---
### TODO
- Ticker 计时器 BUG
- 时间轮，用于定时执行某些任务
- RPC 改为短链接（如果同时出现多个一致目标，不创建新连接。直到所有任务完成关闭短链接）
- RPC 发送，返回对应（因为之前是单线程，一来一回。现在 Role 独立为多线程，存在两个 Role 同时 RPC，需要确定返回的点对点）
- 远程日志存盘系统，因为 Server 这些后面是要分布式的。所以，Logger 系统需要集中远程日志
- Server 与 Server 之间的交互事务机制
- ENet 修改最大连接数，4095 -> 65535
- H5 小游戏 WebSocket 支持
- TCP 支持
---
#### <span id="qstart">1.快速开始</span>
- 1.开发环境中，需要安装 [**.NET8+**](#installenv.1)
- 2.安装，[MongoDB](#installenv.2.1)，根据文件 `./Queen.Server/Res/queen_mongo` [创建数据库](#installenv.2.2)
- 3.此时，如果上述步骤，顺利完成。**支持 .NET8+ 的 IDE** 打开 **`./Queen.sln`** 解决方案，运行 **`Queen.Server.csproj`** 项目即可
#### <span id="installenv">2.环境配置</span>
- ##### <span id="installenv.1">1.安装 .NET</span>
    - 该项目，是基于 .NET8 来开发。因此，需要在开发环境中，安装好 [**.NET8+**](https://dotnet.microsoft.com/zh-cn/download)
    - 同时，MessagePack、Luban 配置工具也是基于 .NET 来开发的。因此，.NET 的环境在接下来的环节中，非常重要，请确保 .NET 开发环境成功配置
- ##### <span id="installenv.2">2.安装/配置 MongoDB</span>
    - <span id="installenv.2.1">1</span>.下载安装 [MongoDB](https://www.mongodb.com/products/self-managed/community-edition)
    - <span id="installenv.2.2">2</span>.数据库配置文件在 `./Queen.Server/Res/queen_mongo` 使用 MongoDB 命令行，顺序执行以下 MongoDB 命令
        - 1.使用 queen 数据库
      ```mongodb
      use queen;
      ```
        - 2.创建 roles 表/文档
      ```mongodb
      db.createCollection("roles", {
        validator: {
          $jsonSchema: {
            bsonType: "object",
            required: ["pid", "username", "password"],
            properties: {
              pid: {
                bsonType: "string",
                description: "must be a string and is required"
              },
              username: {
                bsonType: "string",
                description: "must be a string and is required"
              },
              password: {
                bsonType: "string",
                description: "must be a string and is required"
              },
              nickname: {
                bsonType: "string",
                description: "must be a string and is optional"
              }
            }
          }
        }
      });
      ```
        - 3.约束 roles 表/文档的部分字段为唯一
      ```mongodb
      db.roles.createIndex({ pid: 1 }, { unique: true });
      db.roles.createIndex({ username: 1 }, { unique: true });
      ```
        - 4.创建 datas 表/文档
      ```mongodb
      db.createCollection("datas", {
        validator: {
          $jsonSchema: {
            bsonType: "object",
            required: ["prefix", "value"],
            properties: {
              prefix: {
                bsonType: "string",
                description: "must be a string and is required"
              },
              value: {
                bsonType: "string",
                description: "must be a string and is required"
              },
            }
          }
        }
      });
      ```
        - 4.约束 datas 表/文档的部分字段为唯一
      ```mongodb
      db.datas.createIndex({ prefix: 1 }, { unique: true });
      ```
        - 5.创建 root 用户的权限，用于身份验证
      ```mongodb
      db.createUser({
          user: "root",
          pwd: "root",
          roles: [
            { role: "readWrite", db: "queen" },
            { role: "dbAdmin", db: "queen" }
          ]}
      );
      ```
      完成上述操作，如果顺利的话。数据库已经就绪。
- #### <span id="netproto">3.网络协议</span>
    - ##### <span id="netproto.1">1.定义协议</span>
        - 协议的定义，需要去到 `./Queen.Protocols/` 定义，因为使用的是 **[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)**，只需要定义 `Class/类型` 即可。解读，登录协议，协议文件 `./Queen.Protocols/LoginProto.cs`
        ```csharp
        /// <summary>
        /// 请求登录消息
        /// </summary>
        [MessagePackObject(true)]
        public class C2SLoginMsg : INetMessage
        {
            /// <summary>
            /// 账号
            /// </summary>
            public string username { get; set; }
            /// <summary>
            /// 密码
            /// </summary>
            public string password { get; set; }
        }

        /// <summary>
        /// 响应登录消息
        /// </summay>
        [MessagePackObject(true)]
        public class S2CLoginMsg : INetMessage
        {
            /// <summary>
            /// 操作码/ 1 登录成功，2 用户不存在，3 密码错误
            /// </summary>
            public int code { get; set; }

            /// <summary>
            /// 玩家 ID
            /// </summary>
            public string pid { get; set; }
        }
        ```
        - 定义了 `C2SLoginMsg`、`S2CLoginMsg`，继承 `INetMessage` 接口，这样就定义好了两条协议了。C2S 表示，**ClientToServer**，S2C 表示 **ServerToClient**，使用这个前缀只是为了更好区分协议的流向
        - Class/类型中的结构，全部都是 `get`、`set` 属性。例如，`public string username { get; set; }` 同时，标记 MessagePackObject 特性,`[MessagePackObject(true)]`
        - 有时候，期望复用一些数据结构，又不是协议。那么，不继承 `INetMessage` 接口即可。下方给出示例代码
        ```csharp
        /// <summary>
        /// 数据结构
        /// </summary>
        [MessagePackObject(true)]
        public class Person
        {
            /// <summary>
            /// 姓名
            /// </summary>
            public string name{ get; set; }
            /// <summary>
            /// 年龄
            /// </summary>
            public uint age{ get; set; }
        }
        ```
    - ##### <span id="netproto.2">2.生成协议</span>
        - 因为协议在序列化后，传输，反序列化才能方便读取。协议的包头，需要定义协议号，根据不同的协议号来反序列化。框架底层使用了工具来自动分配协议号 `./Queen.Protocols/Queen.Protocols.Gen`，运行该项目
        - 同时，还需要执行 `./Commands/gen_resolver.bat` 生成静态解析协议的查询表
    - ##### <span id="netproto.3">2.使用协议</span>
        - 消息监听
        ```csharp
        engine.slave.Recv<C2SLoginMsg>(OnC2SLogin);
        ```
        - 消息发送
        ```csharp
        // channel 是端对端的 Socket 连接
        channel.Send(new S2CLoginMsg { pid = pid, code = 1 });
        ```
        - 以上的例子，原生的消息监听及发送。在业务的开发过程中。例如，在未登录的阶段，无法确定玩家身份，只能通过这种方式来包容接收所有的消息及发送。针对确定玩家的消息监听/发送，也是业务最常用的消息监听及发送，详情请看 **[Role]()** 的概念
- #### <span id="servsettings">5.服务器信息配置</span>
打开 `./Queen.Server/Res/settings.json` 文件，进行服务器信息配置
```mongodb
   {
    // 服务器名字
    "name": "queen.server",
    // 主机
    "host": "0.0.0.0",
    // 端口
    "port": 12801,
    // 最大连接数 (最大 4095)
    "maxconn": 4095,
    // 数据库主机
    "dbhost": "127.0.0.1",
    // 数据库端口
    "dbport": 27017,
    // 数据库名
    "dbname": "queen",
    // 数据库用户名
    "dbuser": "root",
    // 数据库密码
    "dbpwd": "root",
    // 数据落地时间间隔（秒）
    "dbsave": 5
  }
```
---
#### <span id="projectdire">项目结构</span>
```text
├─Commands
├─Config
├─Queen
│  ├─3rd
│  ├─Common
│  ├─Core
│  └─Network
├─Queen.Protocols
│  └─Common
├─Queen.Protocols.Gen
├─Queen.Remote
│  └─Res
└─Queen.Server
    ├─Core
    ├─Logic
    ├─Res
    ├─Roles
    └─System
```

- **Commands/** BAT/SHELL，包含但不限，导表、协议生成/导出指令
- **Configs/** 配置表，使用方式，请参考 [Luban](https://github.com/focus-creative-games/luban)
- **Queen/** 核心库
- **Queen.Protocols/** 协议定义
- **Queen.Protocols.Gen/** 协议生成
- **Queen.Remote/** RPC 库
- **Queen.Server/** 业务
