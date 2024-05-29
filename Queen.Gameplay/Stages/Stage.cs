using Queen.Gameplay.Core;
using Queen.Gameplay.Game;
using Queen.Network.Common;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Gameplay.Stages
{
    /// <summary>
    /// 对局房间
    /// </summary>
    public class Stage : Comp
    {
        /// <summary>
        /// 对局房间 ID
        /// </summary>
        private uint id { get; set; }
        /// <summary>
        /// 对局房间名字
        /// </summary>
        private string name { get; set; }
        /// <summary>
        /// 座位信息列表
        /// </summary>
        private SeatInfo[] seatInfos { get; set; }
        /// <summary>
        /// 对局房间管理者
        /// </summary>
        public Director director;

        /// <summary>
        /// Stage 驱动计时器
        /// </summary>
        private uint timingId;

        /// <summary>
        /// 时间流逝
        /// </summary>
        public float elapsedSeconds { get; private set; }

        private Dictionary<uint, Seat> seats = new();
        /// <summary>
        /// 历史帧
        /// </summary>
        private List<FrameInfo> frames { get; set; } = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.slave.Recv<C2G_StartStageMsg>(OnC2SStartStage);
            timingId = engine.ticker.Timing(Tick, 1f / engine.settings.frame, -1);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.slave.UnRecv<C2G_StartStageMsg>(OnC2SStartStage);
            engine.ticker.StopTimer(timingId);
        }

        /// <summary>
        /// 初始化对局房间
        /// </summary>
        /// <param name="id">对局房间 ID</param>
        /// <param name="name">对局房间名字</param>
        /// <param name="seatInfos">对局房间管理者</param>
        public void Initialize(uint id, string name, SeatInfo[] seatInfos)
        {
            this.id = id;
            this.name = name;
            this.seatInfos = seatInfos;
        }

        private void Tick(float tick)
        {
            elapsedSeconds += tick;
        }

        private void OnC2SStartStage(NetChannel channel, C2G_StartStageMsg msg)
        {
            // 不是此房间的 ID 消息
            if (msg.id != id) return;

            // 座位异常
            if (msg.seat >= seatInfos.Length)
            {
                channel.Send(new G2C_StartStageMsg { code = 3 });

                return;
            }

            // 身份验证异常
            var seatInfo = seatInfos[msg.seat];
            if (false == seatInfo.pid.Equals(msg.pid) || false == seatInfo.password.Equals(msg.password))
            {
                channel.Send(new G2C_StartStageMsg { code = 2 });

                return;
            }

            if (false == seats.TryGetValue(msg.seat, out var seat))
            {
                seat = new();
                seats.Add(msg.seat, seat);
            }

            // 顶号，断开连接
            // TODO DEMO 战斗就不做顶号表现了，时间有限
            if (null != seat.channel) seat.channel.Disconnect();
            seat.id = msg.seat;
            seat.channel = channel;

            channel.Send(new G2C_StartStageMsg { code = 1, id = id, frames = frames.ToArray() });
        }

        /// <summary>
        /// 座位
        /// </summary>
        private class Seat
        {
            /// <summary>
            /// 座位 ID
            /// </summary>
            public uint id;
            /// <summary>
            /// 通信渠道
            /// </summary>
            public NetChannel channel;
        }
    }
}
