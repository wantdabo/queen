using Queen.Core;

namespace Queen.ECS;

/// <summary>
/// 世界，管理 Actor 上下文、BehaviorInfo 和 Behavior
/// </summary>
public class Shadow : Comp
{
    /// <summary>
    /// 当前 Actor
    /// </summary>
    public string actor { get; private set; }

    /// <summary>
    /// Actor 的 BehaviorInfo 集合
    /// </summary>
    private Dictionary<string, Dictionary<Type, BehaviorInfo>> infos { get; set; } = new();

    /// <summary>
    /// Behavior 集合
    /// </summary>
    private Dictionary<Type, Behavior> behaviors { get; set; } = new();

    protected override void OnCreate()
    {
        base.OnCreate();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        infos.Clear();
        behaviors.Clear();
    }

    /// <summary>
    /// 进入 Actor 上下文
    /// </summary>
    /// <param name="actor">Actor</param>
    public void Enter(string actor)
    {
        this.actor = actor;
    }

    /// <summary>
    /// 退出 Actor 上下文
    /// </summary>
    public void Exit()
    {
        actor = null;
    }

    /// <summary>
    /// 添加 BehaviorInfo（当前 Actor）
    /// </summary>
    /// <typeparam name="T">BehaviorInfo 类型</typeparam>
    /// <returns>BehaviorInfo</returns>
    public T AddBehaviorInfo<T>() where T : BehaviorInfo, new()
    {
        if (null == actor) return default;

        if (false == infos.TryGetValue(actor, out var dict))
        {
            dict = new();
            infos.Add(actor, dict);
        }

        T info = new();
        info.actor = actor;
        dict[typeof(T)] = info;

        return info;
    }

    /// <summary>
    /// 获取 BehaviorInfo（当前 Actor）
    /// </summary>
    /// <typeparam name="T">BehaviorInfo 类型</typeparam>
    /// <returns>BehaviorInfo</returns>
    public T GetBehaviorInfo<T>() where T : BehaviorInfo
    {
        if (null == actor) return default;
        if (false == infos.TryGetValue(actor, out var dict)) return default;
        if (false == dict.TryGetValue(typeof(T), out var info)) return default;

        return (T)info;
    }

    /// <summary>
    /// 添加 Behavior
    /// </summary>
    /// <typeparam name="T">Behavior 类型</typeparam>
    /// <returns>Behavior</returns>
    public T AddBehavior<T>() where T : Behavior, new()
    {
        T behavior = new();
        behavior.shadow = this;
        behaviors[typeof(T)] = behavior;

        return behavior;
    }

    /// <summary>
    /// 获取 Behavior
    /// </summary>
    /// <typeparam name="T">Behavior 类型</typeparam>
    /// <returns>Behavior</returns>
    public T GetBehavior<T>() where T : Behavior
    {
        if (false == behaviors.TryGetValue(typeof(T), out var behavior)) return default;

        return (T)behavior;
    }
}
