# Queen

这是基于 .NET 的多进程单线程执行的跨平台服务端
在这个高并发，高性能，分布式的时代
有时候，不需要那么高的性能，只需要一个简单，易用的服务器罢了

基础运行环境 **.NET8+**

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

---

### TODOLIST

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
- 3.此时，如果上述步骤，顺利完成。**支持 .NET8+ 的 IDE** 打开 **'./Queen.sln'** 解决方案，运行 **'Queen.Server'** 项目即可

#### <span id="installenv">2.环境配置</span>

- ##### <span id="installenv.1">1.安装 .NET</span>
  
  - 该项目，是基于 .NET8 来开发。因此，需要在开发环境中，安装好 [**.NET8+**](https://dotnet.microsoft.com/zh-cn/download)
  - 同时，MessagePack、Luban 配置工具也是基于 .NET 来开发的。因此，.NET 的环境在接下来的环节中，非常重要，请确保 .NET 开发环境成功配置

- ##### <span id="installenv.2">2.安装/配置 MongoDB</span>
  
  - <span id="installenv.2.1">1</span>.下载安装 [MongoDB](https://www.mongodb.com/products/self-managed/community-edition)
  
  - <span id="installenv.2.2">2</span>.数据库配置文件在 `./Queen.Server/Res/queen_mongo` 使用 MongoDB 命令行，顺序执行以下 MongoDB 命令
    
    - 1.使用 queen 数据库
      
      ```json
      use queen;
      ```
    
    - 2.创建 roles 表/文档
      
      ```json
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
      
      ```json
      db.roles.createIndex({ pid: 1 }, { unique: true });
      db.roles.createIndex({ username: 1 }, { unique: true });
      ```
    
    - 4.创建 datas 表/文档
      
      ```json
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
      
      ```json
      db.datas.createIndex({ prefix: 1 }, { unique: true });
      ```
    
    - 5.创建 root 用户的权限，用于身份验证
      
      ```json
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

- ##### <span id="servsettings">3.服务器信息配置</span>
  
  ```json
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
