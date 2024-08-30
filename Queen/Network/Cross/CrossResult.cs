namespace Queen.Network.Cross;

/// <summary>
/// RPC 状态
/// </summary>
public class CrossState
{
    /// <summary>
    /// 等待
    /// </summary>
    public const ushort Wait = 1;
    /// <summary>
    /// 成功
    /// </summary>
    public const ushort Success = 2;
    /// <summary>
    /// 错误
    /// </summary>
    public const ushort Error = 3;
    /// <summary>
    /// 超时
    /// </summary>
    public const ushort Timeout = 4;
    /// <summary>
    /// 404
    /// </summary>
    public const ushort NotFound = 5;
}

/// <summary>
/// RPC 结果
/// </summary>
public class CrossResult : CrossContentConv
{
    /// <summary>
    /// RPC 状态
    /// </summary>
    public ushort state { get; set; }
    /// <summary>
    /// 传输内容
    /// </summary>
    public string content { get; set; }
}
