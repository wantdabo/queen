namespace Queen.ECS;

/// <summary>
/// 行为数据基类
/// </summary>
public abstract class BehaviorInfo
{
    /// <summary>
    /// 所属 Actor
    /// </summary>
    public string actor { get; set; }

    /// <summary>
    /// 脏标记
    /// </summary>
    public bool dirty { get; set; }
}
