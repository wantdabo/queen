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
    /// 空闲等待的 Client 数量
    /// </summary>
    public ushort idleclientc { get; private set; }
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
    /// 生成客户端节点的队列
    /// </summary>
    private ConcurrentQueue<UDPClient> bornclients = new();
    /// <summary>
    /// 客户端节点
    /// </summary>
    private ConcurrentDictionary<string, UDPClient> clients = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        server = AddComp<UDPServer>();
        server.Initialize(ip, port, false, int.MaxValue, KEY, int.MaxValue);
        server.Create();
        server.Recv<ReqCrossMessage>(OnReqCross);
        engine.eventor.Listen<ExecuteEvent>(OnExecute);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        server.UnRecv<ReqCrossMessage>(OnReqCross);
        server.Destroy();
        engine.eventor.UnListen<ExecuteEvent>(OnExecute);
    }

    /// <summary>
    /// 初始化 RPC
    /// </summary>
    /// <param name="ip">IP 地址</param>
    /// <param name="port">端口</param>
    /// <param name="idleclientc">空闲等待的 Client 数量</param>
    /// <param name="timeout">端口</param>
    public void Initialize(string ip, ushort port, ushort idleclientc, uint timeout)
    {
        this.ip = ip;
        this.port = port;
        this.idleclientc = idleclientc;
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
        if (false == ractions.TryAdd(route, action)) throw new Exception("route can't be repeat.");
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
    /// <returns>RPC 结果</returns>
    public CrossResult CrossAsync(string ip, ushort port, string route, string content)
    {
        var result = new CrossResult();
        CrossAsync(ip, port, route, content, (r) =>
        {
            result.state = r.state;
            result.content = r.content;
        });

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
    public CrossResult CrossAsync<T>(string ip, ushort port, string route, T content) where T : class
    {
        return CrossAsync(ip, port, route, Newtonsoft.Json.JsonConvert.SerializeObject(content));
    }

    /// <summary>
    /// RPC 通信
    /// </summary>
    /// <param name="ip">目标主机 IP</param>
    /// <param name="port">目标主机端口</param>
    /// <param name="route">路径</param>
    /// <param name="content">传输内容</param>
    /// <param name="callback">回调</param>
    public void CrossAsync(string ip, ushort port, string route, string content, Action<CrossResult> callback = null)
    {
        Task.Run(() => callback?.Invoke(CrossSync(ip, port, route, content)));
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
    public void CrossAsync<T>(string ip, ushort port, string route, T content, Action<CrossResult> callback = null) where T : class
    {
        CrossAsync(ip, port, route, Newtonsoft.Json.JsonConvert.SerializeObject(content), callback);
    }

    /// <summary>
    /// RPC 通信
    /// </summary>
    /// <param name="ip">目标主机 IP</param>
    /// <param name="port">目标主机端口</param>
    /// <param name="route">路径</param>
    /// <param name="content">传输内容</param>
    /// <returns>RPC 结果</returns>
    public CrossResult CrossSync(string ip, ushort port, string route, string content)
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
    public CrossResult CrossSync<T>(string ip, ushort port, string route, T content) where T : class
    {
        return CrossSync(ip, port, route, Newtonsoft.Json.JsonConvert.SerializeObject(content));
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
            if (engine.ethread) BebornClients();
            while (true)
            {
                if (bornclients.TryDequeue(out client))
                {
                    clients.TryAdd(key, client);
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
        while (idleclientc > bornclients.Count)
        {
            var client = AddComp<UDPClient>();
            client.Initialize(true);
            client.Create();
            bornclients.Enqueue(client);
        }
    }

    private void OnExecute(ExecuteEvent e)
    {
        BebornClients();
        server.Notify();
    }
}
