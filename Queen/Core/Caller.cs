using System.Collections.Concurrent;

namespace Queen.Core;

/// <summary>
/// 调用器，保证 await 之后回到 Engine 线程
/// </summary>
public class Caller : SynchronizationContext
{
    /// <summary>
    /// 待执行的回调队列
    /// </summary>
    private ConcurrentQueue<Action> queue { get; set; } = new();

    /// <summary>
    /// 投递回调到队列（异步）
    /// </summary>
    /// <param name="d">回调</param>
    /// <param name="state">状态</param>
    public override void Post(SendOrPostCallback d, object state)
    {
        queue.Enqueue(() => d(state));
    }

    /// <summary>
    /// 同步执行回调（直接执行）
    /// </summary>
    /// <param name="d">回调</param>
    /// <param name="state">状态</param>
    public override void Send(SendOrPostCallback d, object state)
    {
        d(state);
    }

    /// <summary>
    /// 执行队列中的所有回调，Engine 每帧调用
    /// </summary>
    public void Pump()
    {
        while (queue.TryDequeue(out var action))
        {
            action();
        }
    }
}
