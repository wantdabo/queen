using Queen.Network.Common;
using Queen.Protocols;
using Queen.Server.Roles.Common;
using Queen.Server.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Rooms
{
    /// <summary>
    /// 房间
    /// </summary>
    public class Room : RoleBehavior
    {
        /// <summary>
        /// 玩家的房间 ID
        /// </summary>
        public uint id
        {
            get
            {
                if (false == engine.sys.landlord.GetRoom(role.pid, out var room)) return 0;

                return room.id;
            }
        }

        /// <summary>
        /// 玩家是房主 ？
        /// </summary>
        public bool owner
        {
            get
            {
                if (false == engine.sys.landlord.GetRoom(id, out var room)) return false;

                return room.owner.Equals(role.pid);
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.sys.eventor.Listen<RoomDestroyEvent>(OnRoomDestroy);
            engine.sys.eventor.Listen<RoomUpdateEvent>(OnRoomUpdate);
            engine.sys.eventor.Listen<RoleJoinEvent>(OnRoleJoin);
            role.Recv<C2S_ExitRoomMsg>(OnC2SExitRoom);
            role.Recv<C2S_KickRoomMsg>(OnC2SKickRoom);
            role.Recv<C2S_JoinRoomMsg>(OnC2SJoinRoom);
            role.Recv<C2S_DestroyRoomMsg>(OnC2SDestroyRoom);
            role.Recv<C2S_CreateRoomMsg>(OnC2SCreateRoom);
            role.Recv<C2S_PullRoomsMsg>(OnC2SPullRooms);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.sys.eventor.UnListen<RoomDestroyEvent>(OnRoomDestroy);
            engine.sys.eventor.UnListen<RoomUpdateEvent>(OnRoomUpdate);
            engine.sys.eventor.UnListen<RoleJoinEvent>(OnRoleJoin);
            role.UnRecv<C2S_ExitRoomMsg>(OnC2SExitRoom);
            role.UnRecv<C2S_KickRoomMsg>(OnC2SKickRoom);
            role.UnRecv<C2S_JoinRoomMsg>(OnC2SJoinRoom);
            role.UnRecv<C2S_DestroyRoomMsg>(OnC2SDestroyRoom);
            role.UnRecv<C2S_CreateRoomMsg>(OnC2SCreateRoom);
            role.UnRecv<C2S_PullRoomsMsg>(OnC2SPullRooms);
        }

        /// <summary>
        /// 推送所有房间信息
        /// </summary>
        private void PushRoomInfos()
        {
            var rooms = new List<Protocols.RoomInfo>();
            foreach (var kv in engine.sys.landlord.rooms)
            {
                var room = new Protocols.RoomInfo();
                room.id = kv.Value.id;
                room.state = kv.Value.state;
                room.owner = kv.Value.owner;
                room.name = kv.Value.name;
                room.needpwd = kv.Value.needpwd;
                room.mlimit = kv.Value.mlimit;
                room.members = new RoomMemberInfo[kv.Value.members.Count];
                var index = 0;
                foreach (var pid in kv.Value.members)
                {
                    room.members[index] = new RoomMemberInfo { pid = pid, nickname = role.nickname };
                    index++;
                }
                rooms.Add(room);
            }

            role.Send(new S2C_PushRoomsMsg { rooms = rooms.ToArray() });
        }

        /// <summary>
        /// 推送房间信息
        /// </summary>
        /// <param name="id">房间 ID</param>
        private void PushRoomInfo(uint id)
        {
            if (false == engine.sys.landlord.GetRoom(id, out var room)) return;

            List<RoomMemberInfo> members = new();
            foreach (var pid in room.members) members.Add(new RoomMemberInfo { pid = pid, nickname = role.nickname });

            role.Send(new S2C_PushRoomMsg
            {
                room = new Protocols.RoomInfo
                {
                    id = room.id,
                    state = room.state,
                    owner = room.owner,
                    name = room.name,
                    needpwd = room.needpwd,
                    mlimit = room.mlimit,
                    members = members.ToArray()
                }
            });
        }

        private void OnRoomDestroy(RoomDestroyEvent e)
        {
            role.Send(new S2C_DestroyRoomMsg { code = 1, id = e.id });
        }

        private void OnRoomUpdate(RoomUpdateEvent e)
        {
            PushRoomInfo(e.id);
        }

        private void OnRoleJoin(RoleJoinEvent e)
        {
            if (false == role.pid.Equals(e.role.pid)) return;

            PushRoomInfos();
        }

        /// <summary>
        /// 请求退出房间消息
        /// </summary>
        /// <param name="msg">消息</param>
        private void OnC2SExitRoom(C2S_ExitRoomMsg msg)
        {
            // 未进入任何房间
            if (false == engine.sys.landlord.GetRoom(role.pid, out var room))
            {
                role.Send(new S2C_ExitRoomMsg { code = 2 });

                return;
            }

            // 游戏进行中
            if (room.state == RoomState.GAMING)
            {
                role.Send(new S2C_ExitRoomMsg { code = 3 });

                return;
            }

            // 退出成功
            role.Send(new S2C_ExitRoomMsg { code = 1 });
            engine.sys.landlord.ExitRoom(role.pid);
        }

        /// <summary>
        /// 请求踢出房间消息
        /// </summary>
        /// <param name="msg">消息</param>
        private void OnC2SKickRoom(C2S_KickRoomMsg msg)
        {
            // 您没有该权限这么做
            if (false == engine.sys.landlord.GetRoom(id, out var room) || false == owner || false == room.owner.Equals(role.pid))
            {
                role.Send(new S2C_KickRoomMsg { code = 4 });

                return;
            }

            // 该成员不存在此房间
            if (false == room.members.Contains(msg.pid))
            {
                role.Send(new S2C_KickRoomMsg { code = 3 });

                return;
            }

            engine.sys.landlord.ExitRoom(msg.pid);

            // 您已被请离房间
            var member = engine.sys.party.GetRole(msg.pid);
            if (null != member)
            {
                member.Send(new S2C_KickRoomMsg { code = 2 });
            }

            // 成员已被请离房间
            role.Send(new S2C_KickRoomMsg { code = 1 });
        }

        /// <summary>
        /// 请求加入房间消息
        /// </summary>
        /// <param name="msg">消息</param>
        private void OnC2SJoinRoom(C2S_JoinRoomMsg msg)
        {
            // 已有加入的房间
            if (engine.sys.landlord.GetRoom(role.pid, out var _))
            {
                role.Send(new S2C_JoinRoomMsg { code = 4 });

                return;
            }

            // 房间不存在
            if (false == engine.sys.landlord.GetRoom(msg.id, out var room))
            {
                role.Send(new S2C_JoinRoomMsg { code = 3 });

                return;
            }

            // 密码错误
            if (room.needpwd && room.password != msg.password)
            {
                role.Send(new S2C_JoinRoomMsg { code = 2 });

                return;
            }

            // 房间成员已满
            if (room.members.Count >= room.mlimit)
            {
                engine.sys.landlord.JoinRoom(msg.id, role.pid, msg.password);
                role.Send(new S2C_JoinRoomMsg { code = 5 });
            }

            // 加入成功
            engine.sys.landlord.JoinRoom(msg.id, role.pid, msg.password);
            role.Send(new S2C_JoinRoomMsg { code = 1 });
        }

        /// <summary>
        /// 请求销毁房间消息
        /// </summary>
        /// <param name="msg">消息</param>
        private void OnC2SDestroyRoom(C2S_DestroyRoomMsg msg)
        {
            // 房间不存在
            if (false == engine.sys.landlord.GetRoom(role.pid, out var room))
            {
                role.Send(new S2C_DestroyRoomMsg { code = 3 });

                return;
            }

            // 没有权限
            if (room.owner != role.pid)
            {
                role.Send(new S2C_DestroyRoomMsg { code = 2 });

                return;
            }

            // 销毁成功
            engine.sys.landlord.DestroyRoom(room.id);
        }

        /// <summary>
        /// 请求创建房间消息
        /// </summary>
        /// <param name="msg">消息</param>
        private void OnC2SCreateRoom(C2S_CreateRoomMsg msg)
        {
            if (engine.sys.landlord.GetRoom(role.pid, out var _))
            {
                role.Send(new S2C_CreateRoomMsg { code = 2 });

                return;
            }

            engine.sys.landlord.CreateRoom(role.pid, msg.name, msg.needpwd, msg.password, msg.mlimit);
            role.Send(new S2C_CreateRoomMsg { code = 1 });
            role.Send(new S2C_JoinRoomMsg { code = 1 });
        }

        /// <summary>
        /// 请求房间列表消息
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <param name="msg">消息</param>
        private void OnC2SPullRooms(C2S_PullRoomsMsg msg)
        {
            PushRoomInfos();
        }
    }
}
