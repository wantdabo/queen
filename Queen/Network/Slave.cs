using Queen.Core;
using Queen.Network.Common;
using Queen.Protocols.Common;
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
        /// 配置主网
        /// </summary>
        /// <param name="ip">地址</param>
        /// <param name="port">端口</param>
        /// <param name="maxConn">最大连接数</param>
        /// <param name="timeout">轮询超时</param>
        public void Initialize(string ip, ushort port, int maxConn = 32, int timeout = 0) 
        {
            engine.logger.Log("slave create.");
            node = new(ip, port, false, maxConn, timeout);
            engine.logger.Log("slave create success.");
        }

        /// <summary>
        /// 轮询
        /// </summary>
        public void Poll()
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
