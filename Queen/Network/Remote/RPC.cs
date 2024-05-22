using Queen.Core;
using Queen.Network.Common;
using Queen.Protocols;
using Queen.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Queen.Network.Remote
{
    /// <summary>
    /// RPC，远程调用
    /// </summary>
    public class RPC : Comp
    {
        /// <summary>
        /// RPC 状态
        /// </summary>
        private enum RPCState
        {
            /// <summary>
            /// 等待
            /// </summary>
            Wait,
            /// <summary>
            /// 成功
            /// </summary>
            Success,
            /// <summary>
            /// 超时
            /// </summary>
            Timeout,
        }

        /// <summary>
        /// RPC 配置
        /// </summary>
        private RPCSettings settings;
        /// <summary>
        /// RPC 影响节点
        /// </summary>
        private ServerNode node;
        /// <summary>
        /// RPC 请求节点
        /// </summary>
        private ClientNode[] clientNodes;

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.eventor.Listen<EngineExecuteEvent>(OnEngineExecute);

            settings = AddComp<RPCSettings>();
            settings.Create();

            engine.logger.Log("rpc create.");
            node = new(settings.host, settings.port, false, settings.maxconn);
            engine.logger.Log("rpc create success.");

            clientNodes = new ClientNode[settings.rpcServs.Length];
            for (int i = 0; i < settings.rpcServs.Length; i++)
            {
                var rpcServ = settings.rpcServs[i];
                var clientNode = new ClientNode();
                clientNode.Connect(rpcServ.host, rpcServ.port);
                clientNodes[i] = clientNode;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.eventor.UnListen<EngineExecuteEvent>(OnEngineExecute);
        }

        private void OnEngineExecute(EngineExecuteEvent e) 
        {
            node.Notify();
        }

        /// <summary>
        /// 注销消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调</param>
        public void UnRecv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            node.UnRecv(action);
        }

        /// <summary>
        /// 注销消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调</param>
        public void Recv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            node.Recv(action);
        }

        /// <summary>
        /// RPC.Call
        /// </summary>
        /// <typeparam name="ST">发送消息类型</typeparam>
        /// <param name="index">RPC 目标下标</param>
        /// <param name="sm">发送消息</param>
        /// <returns>调用成功，YES/NO</returns>
        public bool Call<ST>(int index, ST sm) where ST : INetMessage
        {
            var clientNode = clientNodes[index];
            clientNode.channel.Send(sm);

            return true;
        }

        /// <summary>
        /// RPC.Call
        /// </summary>
        /// <typeparam name="ST">发送消息类型</typeparam>
        /// <typeparam name="RT">接收消息类型</typeparam>
        /// <param name="index">RPC 目标下标</param>
        /// <param name="sm">发送消息</param>
        /// <param name="rm">接收消息</param>
        /// <param name="timeout"></param>
        /// <returns>调用成功，YES/NO</returns>
        public bool Call<ST, RT>(int index, ST sm, out RT rm, uint timeout = 100) where ST : INetMessage where RT : INetMessage
        {
            var state = RPCState.Wait;
            RT result = default;

            var clientNode = clientNodes[index];
            var action = (NetChannel channel, RT msg) =>
            {
                result = msg;
                state = RPCState.Success;
            };
            clientNode.Recv(action);
            clientNode.channel.Send(sm);

            Task.Run(async () =>
            {
                await Task.Delay((int)timeout);
                if (RPCState.Wait == state) state = RPCState.Timeout;
            });

            while (RPCState.Wait == state) { }
            clientNode.UnRecv(action);

            rm = result;
            return RPCState.Success == state;
        }
    }
}
