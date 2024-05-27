using ENet;
using Queen.Network.Common;
using Queen.Protocols.Common;
using Queen.Server.Common;
using Queen.Server.Player.Bags;
using Queen.Server.Player.Rooms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Player.Common
{
    /// <summary>
    /// 玩家
    /// </summary>
    public class Role : Actor
    {
        /// <summary>
        /// 玩家会话
        /// </summary>
        public Session session;
        /// <summary>
        /// 玩家 ID
        /// </summary>
        public string pid;
        /// <summary>
        /// 用户名
        /// </summary>
        public string username;
        /// <summary>
        /// 昵称
        /// </summary>
        public string nickname;
        /// <summary>
        /// 协议映射集合
        /// </summary>
        private Dictionary<Delegate, Delegate> actionMap = new();

        protected override void OnCreate()
        {
            base.OnCreate();

            // 背包
            AddBehavior<Bag>().Create();
            // 房间
            AddBehavior<Room>().Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>
        /// 注销协议接收
        /// </summary>
        /// <typeparam name="T">协议类型</typeparam>
        /// <param name="action">协议回调</param>
        public void UnRecv<T>(Action<T> action) where T : INetMessage
        {
            if (actionMap.TryGetValue(action, out var callback))
            {
                engine.slave.UnRecv(callback as Action<NetChannel, T>);
                actionMap.Remove(action);
            }
        }
        /// <summary>
        /// 注册协议接收
        /// </summary>
        /// <typeparam name="T">协议类型</typeparam>
        /// <param name="action">协议回调</param>
        public void Recv<T>(Action<T> action) where T : INetMessage
        {
            Action<NetChannel, T> callback = (c, m) =>
            {
                if (c.id == session.channel.id && c.peer.ID == session.channel.peer.ID) action?.Invoke(m);
            };
            actionMap.Add(action, callback);
            engine.slave.Recv(callback);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="msg">数据</param>
        public void Send<T>(T msg) where T : INetMessage
        {
            session.channel.Send(msg);
        }
    }
}
