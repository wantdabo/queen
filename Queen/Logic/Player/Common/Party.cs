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
    public struct RoleJoinEvent : IEvent
    {
        public Role role;
    }

    public struct RoleQuitEvent : IEvent
    {
        public Role role;
    }

    public struct RoleJoinInfo
    {
        public NetChannel channel;
        public string pid;
        public string userName;
        public string nickName;
    }

    public class Party : Comp
    {
        public Eventor eventor;

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

        public Role Join(RoleJoinInfo info)
        {
            if (roleMap.TryGetValue(info.pid, out var role)) Quit(role);

            role = AddComp<Role>();
            role.session = AddComp<Session>();
            role.session.channel = info.channel;
            role.session.Create();
            role.pid = info.pid;
            role.userName = info.userName;
            role.nickName = info.nickName;
            role.Create();

            eventor.Tell(new RoleJoinEvent { role = role });

            return role;
        }

        public void Quit(Role role)
        {
            eventor.Tell(new RoleQuitEvent { role = role });

            if (roleMap.ContainsKey(role.pid)) roleMap.Remove(role.pid);
            role.Destroy();
        }

        public Role GetRole(string pid)
        {
            roleMap.TryGetValue(pid, out var role);

            return role;
        }
    }
}
