using Queen.Core;

namespace Queen.Common
{
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

        #region 权重计算
        private struct WeightInfo 
        {
            /// <summary>
            /// Index
            /// </summary>
            public int index;
            /// <summary>
            /// 权重
            /// </summary>
            public int weight;
        }

        private List<WeightInfo> weightInfoTemps = new();

        /// <summary>
        /// 随机权重计算
        /// </summary>
        /// <param name="weights">权重列表</param>
        /// <returns>权重计算结果</returns>
        public int WeightResult(List<int> weights) 
        {
            weightInfoTemps.Clear();
            var totalWeight = 0;
            for (int i = 0; i < weights.Count;) 
            {
                var weightInfo = new WeightInfo { index = weights[i], weight = totalWeight + weights[i + 1] };
                totalWeight += weights[i + 1];
                weightInfoTemps.Add(weightInfo);

                i += 2;
            }

            var result = Range(0, totalWeight);
            foreach (var weightInfo in weightInfoTemps) 
            {
                if (weightInfo.weight >= result) return weightInfo.index;
            }

            throw new Exception("weight result has error.");
        }
        #endregion
    }
}
