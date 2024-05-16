using Queen.Core;
using Queen.Logic.Common;
using Queen.Network.Common;
using Queen.Network.Protocols;
using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network
{
    /// <summary>
    /// 主网组件
    /// </summary>
    public class Slave : Comp
    {
        private ServerNode node;

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.logger.Log("slave create.");
            node = new(engine.cfg.host, engine.cfg.port, false, engine.cfg.maxconn);
            engine.logger.Log("slave create success.");

            Recv<NodeConnectMsg>(OnNodeConnect);
            Recv<NodeDisconnectMsg>(OnNodeDisconnect);
            Recv<NodeTimeoutMsg>(OnNodeTimeout);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            UnRecv<NodeTimeoutMsg>(OnNodeTimeout);
            UnRecv<NodeConnectMsg>(OnNodeConnect);
            UnRecv<NodeDisconnectMsg>(OnNodeDisconnect);
        }

        /// <summary>
        /// 注销消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调</param>
        public void UnRecv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            node.UnListen(action);
        }

        /// <summary>
        /// 注销消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调</param>
        public void Recv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            node.Listen(action);
        }

        /// <summary>
        /// 消息派发
        /// </summary>
        public void Notify()
        {
            node.Notify();
        }

        private void OnNodeConnect(NetChannel channel, NodeConnectMsg msg)
        {
            engine.logger.Log($"have a new connect. channel -> {channel.id}, peer -> {channel.peer.ID}, ipaddress -> {channel.peer.IP}, port -> {channel.peer.Port}");
        }

        private void OnNodeDisconnect(NetChannel channel, NodeDisconnectMsg msg)
        {
            engine.logger.Log($"have a new disconnect. channel -> {channel.id}, peer -> {channel.peer.ID}, ipaddress -> {channel.peer.IP}, port -> {channel.peer.Port}");
        }

        private void OnNodeTimeout(NetChannel channel, NodeTimeoutMsg msg)
        {
            engine.logger.Log($"have a new timeout. channel -> {channel.id}, peer -> {channel.peer.ID}, ipaddress -> {channel.peer.IP}, port -> {channel.peer.Port}");
        }
    }
}
