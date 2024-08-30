using MessagePack;
using Queen.Network.Common;
using Queen.Protocols.Common;

namespace Queen.Network.Cross;

/// <summary>
/// RPC 上下文
/// </summary>
/// <param name="channel">通信渠道</param>
/// <param name="route">路径</param>
/// <param name="content">传输内容</param>
public class CrossContext(NetChannel channel, string route, string content) : CrossContentConv
{
    /// <summary>
    /// 通信渠道
    /// </summary>
    private NetChannel channel { get; set; } = channel;
    /// <summary>
    /// 路径
    /// </summary>
    public string route { get; private set; } = route;
    /// <summary>
    /// 传输内容
    /// </summary>
    public string content { get; private set; } = content;

    /// <summary>
    /// 响应 RPC
    /// </summary>
    /// <param name="state">RPC 状态</param>
    /// <param name="content">传输内容</param>
    /// <typeparam name="T">NewtonJson 的转化类型</typeparam>
    public void Response<T>(ushort state, T content)
    {
        Response(state, Newtonsoft.Json.JsonConvert.SerializeObject(content));
    }

    /// <summary>
    /// 响应 RPC
    /// </summary>
    /// <param name="state">RPC 状态</param>
    /// <param name="content">传输内容</param>
    /// <exception cref="Exception">响应的方法，state 传参必须为 Success 或 Error</exception>
    public void Response(ushort state, string content)
    {
        if (CrossState.Success != state && CrossState.Error != state) throw new Exception("invoke response method, the state of parameter only can be success or error.");
        channel.Send(new ResCrossMessage
        {
            state = state,
            content = content,
        });
    }
}
