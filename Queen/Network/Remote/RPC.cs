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
        /// RPC 目标
        /// </summary>
        public class T
        {
            /// <summary>
            /// RPC.SERVER
            /// </summary>
            public static readonly string SERV = "queen.server";
            /// <summary>
            /// RPC.GAMEPLAY
            /// </summary>
            public static readonly string GAMEPLAY = "queen.gameplay";
        }

        /// <summary>
        /// RPC 状态
        /// </summary>
        public enum RPCState
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
            /// <summary>
            /// 错误
            /// </summary>
            Error,
        }

        /// <summary>
        /// RPC 结果
        /// </summary>
        /// <typeparam name="RT">响应消息类型</typeparam>
        public struct RPCResult<RT> where RT : INetMessage
        {
            /// <summary>
            /// 状态
            /// </summary>
            public RPCState state;
            /// <summary>
            /// 响应的消息
            /// </summary>
            public RT msg;
        }

        /// <summary>
        /// RPC 名字
        /// </summary>
        private string name;
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
        private Dictionary<string, ClientNode> clientNodes = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.eventor.Listen<EngineExecuteEvent>(OnEngineExecute);

            settings = AddComp<RPCSettings>();
            settings.Create();

            engine.logger.Log("rpc create.");
            if (false == settings.servs.TryGetValue(name, out var info))
            {
                engine.logger.Log("rpc create failed.", ConsoleColor.Red);
                throw new Exception("rpc create failed.");
            }

            node = new(info.host, info.port, false, 4095);
            foreach (var serv in settings.servs.Values)
            {
                var clientNode = new ClientNode();
                clientNode.Connect(serv.host, serv.port);
                clientNodes.Add(serv.name, clientNode);
            }
            engine.logger.Log("rpc create success.");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.eventor.UnListen<EngineExecuteEvent>(OnEngineExecute);
        }

        /// <summary>
        /// 配置 RPC
        /// </summary>
        /// <param name="name">RPC 名字</param>
        public void Initialize(string name)
        {
            this.name = name;
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
        /// RPC 调用
        /// </summary>
        /// <typeparam name="ST">发送消息类型</typeparam>
        /// <param name="name">RPC 目标的</param>
        /// <param name="sm">发送的消息</param>
        public void Remote<ST>(string name, ST sm) where ST : INetMessage
        {
            if (false == clientNodes.TryGetValue(name, out var clientNode)) return;
            clientNode.channel.Send(sm);
        }

        /// <summary>
        /// RPC 调用
        /// </summary>
        /// <typeparam name="RT">响应的消息类型</typeparam>
        /// <param name="name">RPC 目标的</param>
        /// <param name="sm">发送的消息</param>
        /// <param name="timeout">超时阈值</param>
        /// <returns>RPC 结果</returns>
        public RPCResult<RT> Remote<RT>(string name, INetMessage sm, uint timeout = 100) where RT : INetMessage
        {
            RPCResult<RT> result = default;
            result.state = RPCState.Wait;

            if (false == clientNodes.TryGetValue(name, out var clientNode))
            {
                result.state = RPCState.Error;

                return result;
            }

            var action = (NetChannel channel, RT msg) =>
            {
                result.msg = msg;
                result.state = RPCState.Success;
            };
            clientNode.Recv(action);
            clientNode.channel.Send(sm);

            Task.Run(async () =>
            {
                await Task.Delay((int)timeout);
                if (RPCState.Wait == result.state) result.state = RPCState.Timeout;
            });

            while (RPCState.Wait == result.state) { }
            clientNode.UnRecv(action);

            return result;
        }

        private void OnEngineExecute(EngineExecuteEvent e)
        {
            node.Notify();
        }
    }
}
