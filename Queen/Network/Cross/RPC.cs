using Queen.Core;
using Queen.Network.Common;
using Queen.Protocols.Common;

namespace Queen.Network.Cross;

/// <summary>
/// RPC 状态
/// </summary>
public enum RPCState
{
    /// <summary>
    /// 等待
    /// </summary>
    Wait,
    /// <summary>
    /// 成功
    /// </summary>
    Success,
    /// <summary>
    /// 超时
    /// </summary>
    Timeout,
    /// <summary>
    /// 错误
    /// </summary>
    Error,
}

/// <summary>
/// RPC
/// </summary>
public class RPC : Comp
{
    /// <summary>
    /// 地址
    /// </summary>
    public string ip { get; set; }
    /// <summary>
    /// 端口
    /// </summary>
    public ushort port { get; set; }
    /// <summary>
    /// 服务器节点
    /// </summary>
    private UDPServer server { get; set; }
    /// <summary>
    /// 客户端节点列表
    /// </summary>
    private Queue<UDPClient> clients = new();

    /// <summary>
    /// 协议映射集合
    /// </summary>
    private Dictionary<Delegate, Delegate> actionDict = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        engine.eventor.Listen<ExecuteEvent>(OnExecute);
        server = AddComp<UDPServer>();
        server.Initialize(ip, port, false, 10000, "QUEEN_RPC", int.MaxValue);
        server.Create();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.eventor.UnListen<ExecuteEvent>(OnExecute);
        server.Destroy();
    }

    public void Initialize(string ip, ushort port)
    {
        this.ip = ip;
        this.port = port;
    }

    /// <summary>
    /// 注销协议接收
    /// </summary>
    /// <typeparam name="T">协议类型</typeparam>
    /// <param name="action">协议回调</param>w
    public void UnRecv<T>(Action<NetChannel, T> action) where T : INetMessage
    {
        engine.EnsureThread();
        server.UnRecv(action);
    }

    /// <summary>
    /// 注册协议接收
    /// </summary>
    /// <typeparam name="T">协议类型</typeparam>
    /// <param name="action">协议回调</param>
    public void Recv<T>(Action<NetChannel, T> action) where T : INetMessage
    {
        engine.EnsureThread();
        server.Recv(action);
    }
    
    public void Remote<ST>(string ip, ushort port, ST sm) where ST : INetMessage
    {
    }

    public (RPCState, RT) Remote<ST, RT>(string ip, ushort port, ST sm, uint timeout = 500) where ST : INetMessage where RT : INetMessage
    {
        return default;
    }
 
    private void OnExecute(ExecuteEvent e)
    {
        server.Notify();
    }
}
