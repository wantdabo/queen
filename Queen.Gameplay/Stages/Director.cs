using Queen.Gameplay.Core;
using Queen.Gameplay.Stages;
using Queen.Network.Common;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Gameplay.Game
{
    /// <summary>
    /// 导演
    /// </summary>
    public class Director : Comp
    {
        /// <summary>
        /// 房间集合
        /// </summary>
        private Dictionary<uint, Stage> stages = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.rpc.Recv<S2G_CreateStageMsg>(OnS2GCreateStage);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.rpc.UnRecv<S2G_CreateStageMsg>(OnS2GCreateStage);
        }

        private void OnS2GCreateStage(NetChannel channel, S2G_CreateStageMsg msg)
        {
            // 房间 ID 重复
            if (stages.ContainsKey(msg.id))
            {
                channel.Send(new G2S_CreateStageMsg { code = 2, id = msg.id });

                return;
            }

            // 创建成功
            var stage = AddComp<Stage>();
            stage.director = this;
            stage.Initialize(msg.id, msg.name, msg.seats);
            stages.Add(msg.id, stage);
            stage.Create();
            channel.Send(new G2S_CreateStageMsg { code = 1, id = msg.id });
        }
    }
}
