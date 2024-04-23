using Queen.Common;
using Queen.Core;
using Queen.Network.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Player.Common
{
    /// <summary>
    /// 玩家加入事件
    /// </summary>
    public struct RoleJoinEvent : IEvent
    {
        /// <summary>
        /// 玩家
        /// </summary>
        public Role role;
    }

    /// <summary>
    /// 玩家退出事件
    /// </summary>
    public struct RoleQuitEvent : IEvent
    {
        /// <summary>
        /// 玩家
        /// </summary>
        public Role role;
    }

    /// <summary>
    /// 玩家加入数据
    /// </summary>
    public struct RoleJoinInfo
    {
        /// <summary>
        /// 通信渠道
        /// </summary>
        public NetChannel channel;
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
    }

    /// <summary>
    /// 派对/ 玩家办事处
    /// </summary>
    public class Party : Comp
    {
        public Eventor eventor;

        /// <summary>
        /// 玩家集合
        /// </summary>
        private Dictionary<string, Role> roleMap = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            eventor = AddComp<Eventor>();
            eventor.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>
        /// 玩家加入
        /// </summary>
        /// <param name="info">玩家加入数据</param>
        /// <returns>玩家实例</returns>
        public Role Join(RoleJoinInfo info)
        {
            if (roleMap.TryGetValue(info.pid, out var role)) Quit(role);

            role = AddComp<Role>();
            role.session = AddComp<Session>();
            role.session.channel = info.channel;
            role.session.Create();
            role.pid = info.pid;
            role.username = info.username;
            role.nickname = info.nickname;
            role.Create();

            roleMap.Add(role.pid, role);
            eventor.Tell(new RoleJoinEvent { role = role });

            return role;
        }

        /// <summary>
        /// 玩家退出
        /// </summary>
        /// <param name="role">玩家实例</param>
        public void Quit(Role role)
        {
            eventor.Tell(new RoleQuitEvent { role = role });

            if (roleMap.ContainsKey(role.pid)) roleMap.Remove(role.pid);
            role.Destroy();
        }

        /// <summary>
        /// 获取玩家
        /// </summary>
        /// <param name="pid">玩家 ID</param>
        /// <returns>玩家实例</returns>
        public Role GetRole(string pid)
        {
            roleMap.TryGetValue(pid, out var role);

            return role;
        }
    }
}
