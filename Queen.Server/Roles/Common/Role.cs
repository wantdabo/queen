using MongoDB.Driver;
using Queen.Common;
using Queen.Common.DB;
using Queen.Network.Common;
using Queen.Protocols.Common;
using Queen.Server.Common;
using Queen.Server.Roles.Bags;
using Queen.Server.Roles.Rooms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Common
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
        /// 密码
        /// </summary>
        public string password;

        /// <summary>
        /// 协议映射集合
        /// </summary>
        private Dictionary<Delegate, Delegate> actionDict = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            eventor.Listen<DBSaveEvent>(OnDBSave);

            // 背包
            AddBehavior<Bag>().Create();
            // 房间
            AddBehavior<Room>().Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            eventor.UnListen<DBSaveEvent>(OnDBSave);
        }

        /// <summary>
        /// 注销协议接收
        /// </summary>
        /// <typeparam name="T">协议类型</typeparam>
        /// <param name="action">协议回调</param>
        public void UnRecv<T>(Action<T> action) where T : INetMessage
        {
            if (actionDict.TryGetValue(action, out var callback))
            {
                engine.slave.UnRecv(callback as Action<NetChannel, T>);
                actionDict.Remove(action);
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
            actionDict.Add(action, callback);
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

        private void OnDBSave(DBSaveEvent e)
        {
            engine.dbo.Replace("roles", Builders<DBRoleValue>.Filter.Eq(p => p.pid, pid), new() { pid = pid, nickname = nickname, username = username, password = password });
        }
    }
}
