using Queen.Network;
using Queen.Protocols.Common;
using Queen.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver;
using Queen.Common.DB;
using Queen.Network.Common;
using Queen.Server.System.Commune;
using System.IO;

namespace Queen.Server.System.Authentication
{
    /// <summary>
    /// 验证器消息适配器
    /// </summary>
    public class AuthenticatorAdpater : Adapter<Authenticator>
    {
        protected override void OnBind()
        {
            bridge.engine.slave.Recv<C2SLoginMsg>(OnC2SLogin);
            bridge.engine.slave.Recv<C2SLogoutMsg>(OnC2SLogout);
            bridge.engine.slave.Recv<C2SRegisterMsg>(OnC2SRegister);
            bridge.engine.slave.Recv<NodeConnectMsg>(OnNodeConnect);
            bridge.engine.slave.Recv<NodeDisconnectMsg>(OnNodeDisconnect);
        }

        protected override void OnUnbind()
        {
            bridge.engine.slave.UnRecv<C2SLoginMsg>(OnC2SLogin);
            bridge.engine.slave.UnRecv<C2SLogoutMsg>(OnC2SLogout);
            bridge.engine.slave.UnRecv<C2SRegisterMsg>(OnC2SRegister);
            bridge.engine.slave.UnRecv<NodeConnectMsg>(OnNodeConnect);
            bridge.engine.slave.UnRecv<NodeDisconnectMsg>(OnNodeDisconnect);
        }

        /// <summary>
        /// 玩家登录
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <param name="msg">消息</param>
        private void OnC2SLogin(NetChannel channel, C2SLoginMsg msg)
        {
            engine.logger.Log($"a user try to login. username -> {msg.username}");

            if (false == bridge.engine.dbo.Find("roles", Builders<DBRoleValue>.Filter.Eq(p => p.username, msg.username), out var values))
            {
                channel.Send(new S2CLoginMsg { code = 2 });
                engine.logger.Log($"the user unregistered. username -> {msg.username}");

                return;
            }

            var reader = values.First();
            if (false == reader.password.Equals(msg.password))
            {
                channel.Send(new S2CLoginMsg { code = 3 });
                engine.logger.Log($"user's password is wrong. username -> {msg.username}");

                return;
            }

            var role = bridge.party.GetRole(reader.pid);
            if (null != role)
            {
                role.session.channel.Send(new S2CLogoutMsg { pid = role.pid, code = 3 });
                bridge.party.Quit(role);
            }

            bridge.party.Join(new RoleJoinInfo { channel = channel, pid = reader.pid, username = reader.username, nickname = reader.nickname, password = reader.password });
            engine.logger.Log($"user login success. pid -> {reader.pid}, username -> {msg.username}");

            channel.Send(new S2CLoginMsg { pid = reader.pid, code = 1 });
        }

        /// <summary>
        /// 玩家登出
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <param name="msg">消息</param>
        private void OnC2SLogout(NetChannel channel, C2SLogoutMsg msg)
        {
            if (null == msg.pid) return;

            var role = bridge.party.GetRole(msg.pid);
            if (null == role)
            {
                engine.logger.Log($"this user is not logged in is no. pid -> {msg.pid}");
                channel.Send(new S2CLogoutMsg { pid = msg.pid, code = 2 });

                return;
            }

            engine.logger.Log($"user logout success. pid -> {msg.pid}");
            bridge.party.Quit(role);
            channel.Send(new S2CLogoutMsg { pid = msg.pid, code = 1 });
        }

        /// <summary>
        /// 玩家注册
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <param name="msg">消息</param>
        private void OnC2SRegister(NetChannel channel, C2SRegisterMsg msg)
        {
            engine.logger.Log($"a new user try to register username -> {msg.username}");
            if (bridge.engine.dbo.Find("roles", Builders<DBRoleValue>.Filter.Eq(p => p.username, msg.username), out var values))
            {
                channel.Send(new S2CRegisterMsg { code = 2 });
                engine.logger.Log($"this username has already been registered. username -> {msg.username}");

                return;
            }

            var pid = Guid.NewGuid().ToString();
            bridge.engine.dbo.Insert<DBRoleValue>("roles", new() { pid = pid, nickname = "", username = msg.username, password = msg.password });

            channel.Send(new S2CRegisterMsg { code = 1 });
            engine.logger.Log($"registration success. pid -> {pid}, username -> {msg.username}");
        }

        private void OnNodeConnect(NetChannel channel, NodeConnectMsg msg)
        {
        }

        private void OnNodeDisconnect(NetChannel channel, NodeDisconnectMsg msg)
        {
            var role = bridge.party.GetRole(channel);
            if (null == role) return;

            engine.logger.Log($"user logout by disconnect. pid -> {role.pid}, username -> {role.username}");
            bridge.party.Quit(role);
        }
    }
}
