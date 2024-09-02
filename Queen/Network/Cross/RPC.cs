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
    /// 客户端节点列表
    /// </summary>
    private ConcurrentQueue<UDPClient> clients = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        server = AddComp<UDPServer>();
        server.Initialize(ip, port, false, 10000, KEY, int.MaxValue);
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
        var client = ApplyClient();
        Action recycle = null;
        var action = (NetChannel channel, ResCrossMessage msg) =>
        {
            if (null == recycle) return;
            recycle?.Invoke();
            recycle = null;
            callback?.Invoke(msg.state, msg.content);
        };
        recycle = () =>
        {
            // RPC 结束, 清理状态 && 断开连接
            client.UnRecv(action);
            client.Disconnect();
            clients.Enqueue(client);
        };

        // UDP 与目标主机建立短链接
        client.Connect(ip, port, KEY);
        client.Recv(action);
        // 发送 RPC 的数据
        client.Send(new ReqCrossMessage
        {
            route = route,
            content = content,
        });
        
        // 超时设定
        Task.Run(async () =>
        {
            await Task.Delay((int)timeout);
            recycle?.Invoke();
            recycle = null;
        });
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
        CrossResult result = new CrossResult { state = CrossState.Wait };
        var action = (NetChannel channel, ResCrossMessage msg) =>
        {
            result.state = msg.state;
            result.content = msg.content;
        };
        var client = ApplyClient();
        // UDP 与目标主机建立短链接
        client.Connect(ip, port, KEY);
        client.Recv(action);
        // 发送 RPC 的数据
        client.Send(new ReqCrossMessage
        {
            route = route,
            content = content,
        });

        // 超时设定
        Task.Run(async () =>
        {
            await Task.Delay((int)timeout);
            result.state = CrossState.Timeout;
        });

        // 等待响应中
        while (CrossState.Wait == result.state) Thread.Sleep(1);

        // RPC 结束, 清理状态 && 断开连接
        client.UnRecv(action);
        client.Disconnect();
        clients.Enqueue(client);

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
    public CrossResult Cross<T>(string ip, ushort port, string route, T content)
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
            channel.Send(new ResCrossMessage { state = CrossState.NotFound });

            return;
        }

        action.Invoke(new CrossContext(channel, msg.route, msg.content));
    }

    /// <summary>
    /// 申请一个新的客户端节点
    /// </summary>
    private UDPClient ApplyClient()
    {
        // 如果池子未有客户端节点，需要等待主线程分配客户端节点
        if (false == clients.TryDequeue(out var client))
        {
            // 申请一个新的客户端节点
            requirecc++;
            if (engine.ethread) BebornClients();
            while (true)
            {
                if (clients.TryDequeue(out client)) break;
                Thread.Sleep(1);
            }
        }

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

            clients.Enqueue(client);
        }
        requirecc = 0;
    }

    private void OnExecute(ExecuteEvent e)
    {
        server.Notify();
        BebornClients();
    }
}
