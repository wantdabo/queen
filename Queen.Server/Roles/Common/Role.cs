using MongoDB.Driver;
using Queen.Common;
using Queen.Common.DB;
using Queen.Network.Common;
using Queen.Protocols;
using Queen.Protocols.Common;
using Queen.Server.Core;
using Queen.Server.Roles.Bags;
using Queen.Server.System.Commune;
using System;
using System.Collections.Concurrent;
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
        /// 事件订阅派发者
        /// </summary>
        public Eventor eventor;

        /// <summary>
        /// 数据自动保存计时器 ID
        /// </summary>
        private uint dbsaveTiming;

        /// <summary>
        /// Jobs 驱动 Task
        /// </summary>
        private Task task = null;

        /// <summary>
        /// 任务列表
        /// </summary>
        private Queue<Action> jobs = new();

        /// <summary>
        /// behaviors 集合
        /// </summary>
        private Dictionary<Type, Behavior> behaviorDict = new();

        /// <summary>
        /// 协议映射集合
        /// </summary>
        private Dictionary<Delegate, Delegate> actionDict = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            eventor = AddComp<Eventor>();
            eventor.Create();
            engine.eventor.Listen<Queen.Core.ExecuteEvent>(OnExecute);
            engine.eventor.Listen<RoleJoinEvent>(OnRoleJoin);
            engine.eventor.Listen<RoleQuitEvent>(OnRoleQuit);
            eventor.Listen<DBSaveEvent>(OnDBSave);
            // 数据写盘
            dbsaveTiming = engine.ticker.Timing((t) => jobs.Enqueue(() => { eventor.Tell<DBSaveEvent>(); }), engine.settings.dbsave, -1);

            // 背包
            AddBehavior<Bag>().Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            // 阻塞，等待任务完成，存盘
            while (null != task && false == task.IsCompleted) Thread.Sleep(1);
            eventor.Tell<DBSaveEvent>();

            engine.eventor.UnListen<Queen.Core.ExecuteEvent>(OnExecute);
            engine.eventor.UnListen<RoleJoinEvent>(OnRoleJoin);
            engine.eventor.UnListen<RoleQuitEvent>(OnRoleQuit);
            eventor.UnListen<DBSaveEvent>(OnDBSave);
            jobs.Clear();
            engine.ticker.StopTimer(dbsaveTiming);
            behaviorDict.Clear();
            actionDict.Clear();
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
                if (c.id == session.channel.id && c.peer.ID == session.channel.peer.ID) jobs.Enqueue(() => { action?.Invoke(m); });
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

        private void OnExecute(Queen.Core.ExecuteEvent e)
        {
            if (null != task && false == task.IsCompleted) return;
            if (false == jobs.TryDequeue(out var job)) return;

            // 开启任务线程
            task = Task.Run(() =>
            {
                job.Invoke();

                return Task.CompletedTask;
            });
        }

        private void OnRoleJoin(RoleJoinEvent e)
        {
            jobs.Enqueue(() => eventor.Tell(e));
        }

        private void OnRoleQuit(RoleQuitEvent e)
        {
            jobs.Enqueue(() => eventor.Tell(e));
        }

        private void OnDBSave(DBSaveEvent e)
        {
            engine.dbo.Replace("roles", Builders<DBRoleValue>.Filter.Eq(p => p.pid, pid), new() { pid = pid, nickname = nickname, username = username, password = password });
        }
    }
}
