using Queen.Core;
using Queen.Logic.Common;
using Queen.Network.Common;
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
            node = new(engine.cfg.host, engine.cfg.port, false, engine.cfg.maxConn);
            engine.logger.Log("slave create success.");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
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
    }
}
