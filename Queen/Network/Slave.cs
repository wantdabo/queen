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
            engine.eventor.Listen<ExecuteEvent>(OnExecute);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.eventor.UnListen<ExecuteEvent>(OnExecute);
        }

        /// <summary>
        /// 配置主网
        /// </summary>
        /// <param name="ip">地址</param>
        /// <param name="port">端口</param>
        /// <param name="maxConn">最大连接数</param>
        /// <param name="timeout">轮询超时</param>
        public void Initialize(string ip, ushort port, int maxConn = 32, int timeout = 1) 
        {
            node = new(ip, port, false, maxConn, timeout);
        }

        private void OnExecute(ExecuteEvent e)
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
            engine.EnsureThread();
            node.UnRecv(action);
        }

        /// <summary>
        /// 注销消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调</param>
        public void Recv<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            engine.EnsureThread();
            node.Recv(action);
        }
    }
}
