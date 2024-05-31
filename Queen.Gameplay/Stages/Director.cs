using Queen.Gameplay.Core;
using Queen.Gameplay.Stages;
using Queen.Network.Common;
using Queen.Network.Remote;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
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
            engine.rpc.Recv<S2G_DestroyStageMsg>(OnC2GDestroyStage);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.rpc.UnRecv<S2G_CreateStageMsg>(OnS2GCreateStage);
            engine.rpc.UnRecv<S2G_DestroyStageMsg>(OnC2GDestroyStage);
        }

        private void OnS2GCreateStage(NetChannel channel, S2G_CreateStageMsg msg)
        {
            // 对局房间重复
            if (stages.ContainsKey(msg.id))
            {
                channel.Send(new G2S_CreateStageMsg { code = 2, id = msg.id });

                return;
            }

            // 正式项目，此处应该开多线程会好些
            // 创建成功
            var stage = AddComp<Stage>();
            stage.director = this;
            stage.Initialize(msg.id, msg.name, msg.seats);
            stages.Add(msg.id, stage);
            stage.Create();
            channel.Send(new G2S_CreateStageMsg { code = 1, id = msg.id });

            engine.logger.Log($"create a new stage. id -> {msg.id}, seatc -> {msg.seats.Count()}");
        }

        private void OnC2GDestroyStage(NetChannel channel, S2G_DestroyStageMsg msg)
        {
            DestroyRoom(msg.id);
            engine.logger.Log($"destroy the stage. id -> {msg.id}");
        }

        /// <summary>
        /// 销毁对局房间
        /// </summary>
        /// <param name="id">房间 ID</param>
        public void DestroyRoom(uint id)
        {
            // 对局房间销毁
            if (stages.TryGetValue(id, out var stage))
            {
                stage.Destroy();
                RmvComp(stage);
                stages.Remove(id);
            }

            // 通知 SERV 销毁房间
            engine.rpc.Execute(RPC.TAR.SERV, new G2S_DestroyStageMsg { id = id });
        }
    }
}
