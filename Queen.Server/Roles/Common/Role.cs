using MongoDB.Driver;
using Queen.Common;
using Queen.Common.DB;
using Queen.Network.Common;
using Queen.Protocols;
using Queen.Protocols.Common;
using Queen.Server.Core;
using Queen.Server.Roles.Bags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Common
{
    /// <summary>
    /// 数据保存事件
    /// </summary>
    public struct DBSaveEvent : IEvent { }

    /// <summary>
    /// 玩家
    /// </summary>
    public class Role : Comp
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

        /// <summary>
        /// 事件订阅派发者
        /// </summary>
        public Eventor eventor;

        /// <summary>
        /// behaviors 集合
        /// </summary>
        private Dictionary<Type, Behavior> behaviorDict = new();

        private uint dbsaveTiming;

        protected override void OnCreate()
        {
            base.OnCreate();
            eventor = AddComp<Eventor>();
            eventor.Create();
            eventor.Listen<DBSaveEvent>(OnDBSave);
            // 数据写盘
            dbsaveTiming = engine.ticker.Timing((t) => eventor.Tell<DBSaveEvent>(), engine.settings.dbsave, -1);

            // 背包
            AddBehavior<Bag>().Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            eventor.UnListen<DBSaveEvent>(OnDBSave);
            engine.ticker.StopTimer(dbsaveTiming);
            behaviorDict.Clear();
        }

        /// <summary>
        /// 获取 Behavior
        /// </summary>
        /// <typeparam name="T">Behavior 类型</typeparam>
        /// <returns>Behavior 实例</returns>
        public T GetBehavior<T>() where T : Behavior
        {
            if (false == behaviorDict.TryGetValue(typeof(T), out var behavior)) return null;

            return behavior as T;
        }

        /// <summary>
        /// 添加 Behavior
        /// </summary>
        /// <typeparam name="T">Behavior 类型</typeparam>
        /// <returns>Behavior 实例</returns>
        /// <exception cref="Exception">不能添加重复的 Behavior</exception>
        public T AddBehavior<T>() where T : Behavior, new()
        {
            if (behaviorDict.ContainsKey(typeof(T))) throw new Exception("can't add repeat behavior.");

            T behavior = AddComp<T>();
            behavior.role = this;
            behaviorDict.Add(typeof(T), behavior);

            return behavior;
        }

        /// <summary>
        /// 注销协议接收
        /// </summary>
        /// <typeparam name="T">协议类型</typeparam>
        /// <param name="action">协议回调</param>w
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
            engine.slave.Recv(callback);
            actionDict.Add(action, callback);
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
