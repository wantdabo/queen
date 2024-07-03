using Queen.Common;
using Queen.Network.Common;
using Queen.Server.Core;
using Queen.Server.Roles.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.System
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
        /// 昵称
        /// </summary>
        public string nickname;
        /// <summary>
        /// 用户名
        /// </summary>
        public string username;
        /// <summary>
        /// 密码
        /// </summary>
        public string password;
    }

    /// <summary>
    /// 派对/ 玩家办事处
    /// </summary>
    public class Party : Comp
    {
        /// <summary>
        /// 玩家集合
        /// </summary>
        private Dictionary<string, Role> roleDict = new();

        /// <summary>
        /// 玩家加入
        /// </summary>
        /// <param name="info">玩家加入数据</param>
        /// <returns>玩家</returns>
        public Role Join(RoleJoinInfo info)
        {
            if (roleDict.TryGetValue(info.pid, out var role)) Quit(role);

            role = AddComp<Role>();
            role.session = role.AddComp<Session>();
            role.session.channel = info.channel;
            role.session.Create();
            role.pid = info.pid;
            role.nickname = info.nickname;
            role.username = info.username;
            role.password = info.password;
            role.Create();

            roleDict.Add(role.pid, role);
            engine.eventor.Tell(new RoleJoinEvent { role = role });

            return role;
        }

        /// <summary>
        /// 玩家退出
        /// </summary>
        /// <param name="role">玩家</param>
        public void Quit(Role role)
        {
            engine.eventor.Tell(new RoleQuitEvent { role = role });

            if (roleDict.ContainsKey(role.pid)) roleDict.Remove(role.pid);
            role.Destroy();
        }

        /// <summary>
        /// 获取玩家
        /// </summary>
        /// <param name="pid">玩家 ID</param>
        /// <returns>玩家</returns>
        public Role GetRole(string pid)
        {
            roleDict.TryGetValue(pid, out var role);

            return role;
        }

        /// <summary>
        /// 获取玩家
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <returns>玩家</returns>
        public Role GetRole(NetChannel channel)
        {
            foreach (var kv in roleDict)
            {
                if (channel.peer.ID == kv.Value.session.channel.peer.ID) return kv.Value;
            }

            return null;
        }
    }
}
