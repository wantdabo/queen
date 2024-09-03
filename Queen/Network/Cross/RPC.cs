using Queen.Core;
using Queen.Network.Common;
using Queen.Protocols.Common;
using System.Collections.Concurrent;

namespace Queen.Network.Cross;

/// <summary>
/// RPC
/// </summary>
public class RPC : Comp
{
    /// <summary>
    /// RPC 连接 KEY
    /// </summary>
    private readonly string KEY = "QUEEN_RPC";
    /// <summary>
    /// 地址
    /// </summary>
    public string ip { get; private set; }
    /// <summary>
    /// 端口
    /// </summary>
    public ushort port { get; private set; }
    /// <summary>
    /// 超时设定
    /// </summary>
    public uint timeout { get; private set; }
    /// <summary>
    /// 服务器节点
    /// </summary>
    private UDPServer server { get; set; }
    /// <summary>
    /// 路由方法列表
    /// </summary>
    private Dictionary<string, Action<CrossContext>> ractions = new();
    /// <summary>
    /// 客户端节点需求新增
    /// </summary>
    private uint requirecc { get; set; } = 0;
    /// <summary>
    /// 生成客户端节点的队列
    /// </summary>
    private ConcurrentQueue<UDPClient> bornclients = new();
    /// <summary>
    /// 客户端节点
    /// </summary>
    private Dictionary<string, UDPClient> clients = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        server = AddComp<UDPServer>();
        server.Initialize(ip, port, false, int.MaxValue, KEY, int.MaxValue);
        server.Create();
        engine.eventor.Listen<ExecuteEvent>(OnExecute);
        server.Recv<ReqCrossMessage>(OnReqCross);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.eventor.UnListen<ExecuteEvent>(OnExecute);
        server.UnRecv<ReqCrossMessage>(OnReqCross);
        server.Destroy();
    }

    /// <summary>
    /// 初始化 RPC
    /// </summary>
    /// <param name="ip">IP 地址</param>
    /// <param name="port">端口</param>
    /// <param name="timeout">端口</param>
    public void Initialize(string ip, ushort port, uint timeout)
    {
        this.ip = ip;
        this.port = port;
        this.timeout = timeout;
    }

    /// <summary>
    /// 监听路由
    /// </summary>
    /// <param name="route">路径</param>
    /// <param name="action">路由动作</param>
    /// <exception cref="Exception">不能添加重复的路径</exception>
    public void Routing(string route, Action<CrossContext> action)
    {
        if (ractions.ContainsKey(route)) throw new Exception("route can't be repeat.");
        ractions.Add(route, action);
    }

    /// <summary>
    /// 注销路由
    /// </summary>
    /// <param name="route">路径</param>
    /// <param name="action">路由动作</param>
    private void UnRouting(string route, Action<CrossContext> action)
    {
        if (false == ractions.ContainsKey(route)) return;
        ractions.Remove(route);
    }

    /// <summary>
    /// RPC 通信
    /// </summary>
    /// <param name="ip">目标主机 IP</param>
    /// <param name="port">目标主机端口</param>
    /// <param name="route">路径</param>
    /// <param name="content">传输内容</param>
    /// <param name="callback">回调</param>
    public void Cross(string ip, ushort port, string route, string content, Action<ushort, string> callback = null)
    {
        var id = Guid.NewGuid().ToString();
        var client = GetClient(ip, port);
        bool istimeout = false;
        Action<NetChannel, ResCrossMessage> action = null;
        action = (channel, msg) =>
        {
            if (istimeout) return;
            if (msg.id != id) return;
            
            client.UnRecv(action);
            callback?.Invoke(msg.state, msg.content);
        };

        client.Recv(action);
        // 发送 RPC 的数据
        client.Send(new ReqCrossMessage
        {
            id = id,
            route = route,
            content = content,
        });

        // 超时设定
        Task.Run(async () =>
        {
            await Task.Delay((int)timeout);
            istimeout = true;
            client.UnRecv(action);
        });
    }

    /// <summary>
    /// RPC 通信
    /// </summary>
    /// <param name="ip">目标主机 IP</param>
    /// <param name="port">目标主机端口</param>
    /// <param name="route">路径</param>
    /// <param name="content">传输内容</param>
    /// <param name="callback">回调</param>
    /// <typeparam name="T">NewtonJson 的转化类型</typeparam>
    public void Cross<T>(string ip, ushort port, string route, T content, Action<ushort, string> callback = null) where T : class
    {
        Cross(ip, port, route, Newtonsoft.Json.JsonConvert.SerializeObject(content), callback);
    }

    /// <summary>
    /// RPC 通信
    /// </summary>
    /// <param name="ip">目标主机 IP</param>
    /// <param name="port">目标主机端口</param>
    /// <param name="route">路径</param>
    /// <param name="content">传输内容</param>
    /// <returns>RPC 结果</returns>
    public CrossResult Cross(string ip, ushort port, string route, string content)
    {
        var id = Guid.NewGuid().ToString();
        CrossResult result = new CrossResult { state = CROSS_STATE.WAIT };
        var action = (NetChannel channel, ResCrossMessage msg) =>
        {
            if (msg.id != id) return;

            result.state = msg.state;
            result.content = msg.content;
        };
        var client = GetClient(ip, port);
        client.Recv(action);
        // 发送 RPC 的数据
        client.Send(new ReqCrossMessage
        {
            id = id,
            route = route,
            content = content,
        });

        // 超时设定
        Task.Run(async () =>
        {
            await Task.Delay((int)timeout);
            result.state = CROSS_STATE.TIMEOUT;
        });

        // 等待响应中
        while (CROSS_STATE.WAIT == result.state) Thread.Sleep(1);

        // RPC 结束, 清理状态 && 断开连接
        client.UnRecv(action);

        return result;
    }

    /// <summary>
    /// RPC 通信
    /// </summary>
    /// <param name="ip">目标主机 IP</param>
    /// <param name="port">目标主机端口</param>
    /// <param name="route">路径</param>
    /// <param name="content">传输内容</param>
    /// <typeparam name="T">NewtonJson 的转化类型</typeparam>
    /// <returns>RPC 结果</returns>
    public CrossResult Cross<T>(string ip, ushort port, string route, T content) where T : class
    {
        return Cross(ip, port, route, Newtonsoft.Json.JsonConvert.SerializeObject(content));
    }

    /// <summary>
    /// 收到 RPC 消息
    /// </summary>
    /// <param name="channel">通信渠道</param>
    /// <param name="msg">消息</param>
    private void OnReqCross(NetChannel channel, ReqCrossMessage msg)
    {
        if (false == ractions.TryGetValue(msg.route, out var action))
        {
            channel.Send(new ResCrossMessage { id = msg.id, state = CROSS_STATE.NOTFOUND });

            return;
        }

        action.Invoke(new CrossContext(channel, msg.id, msg.route, msg.content));
    }

    /// <summary>
    /// 申请一个新的客户端节点
    /// </summary>
    /// <param name="ip">IP 地址</param>
    /// <param name="port">端口</param>
    private UDPClient GetClient(string ip, ushort port)
    {
        var key = $"{ip}:{port}";
        // 如果池子未有客户端节点，需要等待主线程分配客户端节点
        if (false == clients.TryGetValue(key, out var client))
        {
            // 申请一个新的客户端节点
            requirecc++;
            if (engine.ethread) BebornClients();
            while (true)
            {
                if (bornclients.TryDequeue(out client))
                {
                    clients.Add(key, client);
                    break;
                }
                Thread.Sleep(1);
            }
        }

        // UDP 与目标主机建立短链接
        if (false == client.connected) client.Connect(ip, port, KEY);

        return client;
    }

    /// <summary>
    /// 生成需求的客户端节点数
    /// </summary>
    private void BebornClients()
    {
        // 新增客户端节点（需要在主线才能新增）
        if (0 > requirecc) return;
        for (int i = 0; i < requirecc; i++)
        {
            var client = AddComp<UDPClient>();
            client.Initialize(true);
            client.Create();

            bornclients.Enqueue(client);
        }
        requirecc = 0;
    }

    private void OnExecute(ExecuteEvent e)
    {
        BebornClients();
        server.Notify();
    }
}
