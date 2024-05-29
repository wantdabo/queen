using Queen.Common;
using Queen.Network.Common;
using Queen.Network.Remote;
using Queen.Protocols;
using Queen.Server.Common;
using Queen.Server.Roles.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.System
{
    #region Events
    /// <summary>
    /// 房间销毁事件
    /// </summary>
    public struct RoomDestroyEvent : IEvent
    {
        /// <summary>
        /// 房间 ID
        /// </summary>
        public uint id;
    }

    /// <summary>
    /// 房间更新事件
    /// </summary>
    public struct RoomUpdateEvent : IEvent
    {
        /// <summary>
        /// 房间 ID
        /// </summary>
        public uint id;
    }
    #endregion

    #region Defines
    /// <summary>
    /// 房间状态
    /// </summary>
    public class RoomState
    {
        /// <summary>
        /// 等待加入
        /// </summary>
        public static readonly int WAITING = 1;
        /// <summary>
        /// 游戏进行
        /// </summary>
        public static readonly int GAMING = 2;
    }

    /// <summary>
    /// 房间信息
    /// </summary>
    public class RoomInfo
    {
        /// <summary>
        /// 房间 ID
        /// </summary>
        public uint id { get; set; }
        /// <summary>
        /// 房间状态/ 1 等待加入，2 游戏进行中
        /// </summary>
        public int state { get; set; }
        /// <summary>
        /// 房主
        /// </summary>
        public string owner { get; set; }
        /// <summary>
        /// 房间名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 需要密码
        /// </summary>
        public bool needpwd { get; set; }
        /// <summary>
        /// 房间密码
        /// </summary>
        public uint password { get; set; }
        /// <summary>
        /// 房间人数
        /// </summary>
        public int mlimit { get; set; }
        /// <summary>
        /// 房间成员
        /// </summary>
        public List<string> members { get; set; }
    }
    #endregion

    /// <summary>
    /// 房东
    /// </summary>
    public class Landlord : Behavior
    {
        /// <summary>
        /// 自增 ID
        /// </summary>
        private uint incrementId { get; set; } = 100000;
        /// <summary>
        /// 房间列表
        /// </summary>
        public Dictionary<uint, RoomInfo> rooms { get; private set; } = new();
        /// <summary>
        /// 开局的座位信息
        /// </summary>
        public Dictionary<string, SeatInfo> seats { get; private set; } = new();

        /// <summary>
        /// 退出房间
        /// </summary>
        /// <param name="pid">玩家 ID</param>
        public void ExitRoom(string pid)
        {
            if (false == GetRoom(pid, out var room)) return;

            // 对局中，不允许退出房间
            if (RoomState.GAMING == room.state) return;

            room.members.Remove(pid);

            if (room.members.Count <= 0)
            {
                DestroyRoom(room.id);
                actor.eventor.Tell(new RoomDestroyEvent { id = room.id });

                return;
            }

            if (pid.Equals(room.owner)) room.owner = room.members.First();
            actor.eventor.Tell(new RoomUpdateEvent { id = room.id });
        }

        /// <summary>
        /// 加入房间
        /// </summary>
        /// <param name="id">房间 ID</param>
        /// <param name="pid">玩家 ID</param>
        /// <param name="password">密码</param>
        public void JoinRoom(uint id, string pid, uint password)
        {
            if (false == rooms.TryGetValue(id, out var room)) return;
            if (room.needpwd && room.password != password) return;
            if (room.members.Count >= room.mlimit) return;
            if (room.members.Contains(pid)) return;

            room.members.Add(pid);
            actor.eventor.Tell(new RoomUpdateEvent { id = room.id });
        }

        /// <summary>
        /// 销毁房间
        /// </summary>
        /// <param name="id">房间 ID</param>
        public void DestroyRoom(uint id)
        {
            if (false == rooms.ContainsKey(id)) return;

            rooms.Remove(id);
            actor.eventor.Tell(new RoomDestroyEvent { id = id });
        }

        /// <summary>
        /// 创建房间
        /// </summary>
        /// <param name="owner">房主</param>
        /// <param name="name">房间名字</param>
        /// <param name="needpwd">需要密码</param>
        /// <param name="password">密码</param>
        /// <param name="mlimit">人数限制</param>
        public void CreateRoom(string owner, string name, bool needpwd, uint password, int mlimit)
        {
            if (GetRoom(owner, out var room)) return;

            room = new RoomInfo();
            room.id = ++incrementId;
            room.state = RoomState.WAITING;
            room.owner = owner;
            room.name = name;
            room.needpwd = needpwd;
            room.password = password;
            room.mlimit = mlimit;
            room.members = new();
            rooms.Add(room.id, room);

            // 自动加入房间
            JoinRoom(room.id, owner, password);
        }

        /// <summary>
        /// 房间开局
        /// </summary>
        /// <param name="id">房间 ID</param>
        /// <returns>开局成功</returns>
        public bool Room2Game(uint id)
        {
            if (false == GetRoom(id, out var room)) return false;
            if (RoomState.GAMING == room.state) return false;

            List<SeatInfo> seatInfos = new();
            uint index = 0;
            foreach (var member in room.members)
            {
                var role = engine.sys.party.GetRole(member);
                var seat = new SeatInfo
                {
                    seat = index,
                    pid = role.pid,
                    nickname = role.nickname,
                    password = Guid.NewGuid().ToString()
                };
                seatInfos.Add(seat);
                index++;
            }

            // RPC 调用 GAMEPLAY 开局
            var result = engine.rpc.Execute<S2G_CreateStageMsg, G2S_CreateStageMsg>(RPC.TAR.GAMEPLAY, new() { id = room.id, name = room.name, seats = seatInfos.ToArray() });
            if (RPCState.Success != result.state) return false;
            foreach (var seatInfo in seatInfos) seats.Add(seatInfo.pid, seatInfo);
            // 修改房间状态
            room.state = RoomState.GAMING;
            actor.eventor.Tell(new RoomUpdateEvent { id = room.id });

            return true;
        }

        /// <summary>
        /// 获取房间
        /// </summary>
        /// <param name="id">房间 ID</param>
        /// <param name="room">房间</param>
        /// <returns>是否存在房间</returns>
        public bool GetRoom(uint id, out RoomInfo room)
        {
            return rooms.TryGetValue(id, out room);
        }

        /// <summary>
        /// 获取房间
        /// </summary>
        /// <param name="pid">玩家 ID</param>
        /// <param name="room">房间</param>
        /// <returns>是否存在房间</returns>
        public bool GetRoom(string pid, out RoomInfo room)
        {
            room = default;
            foreach (var kv in rooms)
            {
                if (kv.Value.members.Contains(pid))
                {
                    room = kv.Value;

                    return true;
                }
            }

            return false;
        }
    }
}