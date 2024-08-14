using Queen.Common;
using Queen.Core;
using Queen.Network.Common;
using Queen.Server.Core;
using Queen.Server.Roles.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.System.Commune
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
        public string uuid;
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
    public class Party : Sys
    {
        /// <summary>
        /// 玩家集合
        /// </summary>
        private Dictionary<string, Role> roleDict = new();

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
        /// 玩家加入
        /// </summary>
        /// <param name="info">玩家加入数据</param>
        /// <returns>玩家</returns>
        public void Join(RoleJoinInfo info)
        {
            var role = GetRole(info.uuid);
            if (null != role) Quit(role);
            
            if (null == role)
            {
                role = AddComp<Role>();
                role.session = role.AddComp<Session>();
                role.session.Create();
                role.Initialize(new() { uuid = info.uuid, username = info.username, nickname = info.nickname, password = info.password });
                role.Create();
                roleDict.Add(role.info.uuid, role);
            }
            role.session.channel = info.channel;
            
            if (role.online) return;
            engine.eventor.Tell(new RoleJoinEvent { role = role });
        }

        /// <summary>
        /// 玩家退出
        /// </summary>
        /// <param name="role">玩家</param>
        public void Quit(Role role)
        {
            if (false == role.online) return;
            engine.eventor.Tell(new RoleQuitEvent { role = role });
        }

        /// <summary>
        /// 获取玩家
        /// </summary>
        /// <param name="uuid">玩家 ID</param>
        /// <returns>玩家</returns>
        public Role GetRole(string uuid)
        {
            roleDict.TryGetValue(uuid, out var role);

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
                if (channel.id == kv.Value.session.channel.id) return kv.Value;
            }

            return null;
        }

        private void OnExecute(ExecuteEvent e)
        {

        }
    }
}
