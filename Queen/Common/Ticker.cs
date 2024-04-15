using Queen.Core;
using System;

namespace Queen.Common
{
    /// <summary>
    /// 计时器
    /// </summary>
    public class Ticker : Comp
    {
        private Timer timer;

        protected override void OnCreate()
        {
            base.OnCreate();
            var tick = 100;
            timer = new Timer(new TimerCallback((state) =>
            {
                // 驱动计时器
                TickTimerInfos(timerInfos, tick);
                timer.Change(tick, 0);
            }), null, tick, 0);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>
        /// 计时器结构体
        /// </summary>
        private struct TimerInfo
        {
            /// <summary>
            /// ID
            /// </summary>
            public uint id;
            /// <summary>
            /// Callback/回调
            /// </summary>
            public Action<float> action;
            /// <summary>
            /// 触发所需的时间
            /// </summary>
            public float duration;
            /// <summary>
            /// 当前过去了多少时间
            /// </summary>
            public float elapsed;
            /// <summary>
            /// 循环次数（设置负数为将会一直循环，例如 -1）
            /// </summary>
            public int loop;
        }

        private uint timerIncrementId = 0;
        private List<TimerInfo> timerInfos = new();

        /// <summary>
        /// 停止计时器
        /// </summary>
        /// <param name="id">计时器 ID</param>
        /// <param name="tickDef">计时器类型</param>
        public void StopTimer(uint id)
        {
            if (0 == timerInfos.Count) return;

            for (int i = timerInfos.Count - 1; i >= 0; i--)
            {
                var info = timerInfos[i];
                if (id != info.id) continue;

                timerInfos.RemoveAt(i);
                break;
            }
        }

        /// <summary>
        /// 开始计时
        /// </summary>
        /// <param name="action">回调</param>
        /// <param name="duration">触发所需的时间</param>
        /// <param name="loop">循环次数（设置负数为将会一直循环，例如 -1）</param>
        /// <returns>计时器 ID</returns>
        public uint Timing(Action<float> action, float duration, int loop)
        {
            timerIncrementId++;

            TimerInfo info = new()
            {
                id = timerIncrementId,
                action = action,
                duration = duration,
                loop = loop
            };
            timerInfos.Add(info);

            return info.id;
        }

        private void TickTimerInfos(List<TimerInfo> infos, float tick)
        {
            if (0 == infos.Count) return;

            for (int i = infos.Count - 1; i >= 0; i--)
            {
                var info = infos[i];
                info.elapsed += tick;
                infos[i] = info;
                if (info.duration > info.elapsed) continue;

                info.elapsed = Math.Max(0, info.elapsed - info.duration);
                info.loop--;
                infos[i] = info;
                if (0 == info.loop) infos.RemoveAt(i);

                info.action.Invoke(tick);
            }
        }
    }
}
