using Queen.Core;

namespace Queen.Common;

/// <summary>
/// 随机器
/// </summary>
public class Random : Comp
{
    private System.Random random;

    /// <summary>
    /// 随机种子
    /// </summary>
    public int seed { get; private set; } = -1;

    protected override void OnCreate()
    {
            base.OnCreate();
            seed = int.MaxValue;
            random = new System.Random(seed);
        }

    protected override void OnDestroy()
    {
            base.OnDestroy();
        }

    /// <summary>
    /// 整数范围随机
    /// </summary>
    /// <param name="min">最小值</param>
    /// <param name="max">最大值</param>
    /// <returns>结果</returns>
    public int Range(int min, int max)
    {
            return random.Next(min, max);
        }

    /// <summary>
    /// 浮点数范围随机
    /// </summary>
    /// <param name="min">最小值</param>
    /// <param name="max">最大值</param>
    /// <returns>结果</returns>
    public float Range(float min, float max)
    {
            return random.Next((int)(min * engine.cfg.float2Int), (int)(max * engine.cfg.float2Int)) * engine.cfg.int2Float;
        }
}