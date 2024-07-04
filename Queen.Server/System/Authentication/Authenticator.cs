using MongoDB.Driver;
using Queen.Common.DB;
using Queen.Network.Common;
using Queen.Protocols;
using Queen.Protocols.Common;
using Queen.Server.Core;
using Queen.Server.System.Commune;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.System.Authentication
{
    /// <summary>
    /// 登录
    /// </summary>
    public class Authenticator : Comp
    {
        public Party party;

        protected override void OnCreate()
        {
            base.OnCreate();
            var adapter = AddComp<AuthenticatorAdpater>();
            adapter.Initialize(this);
            adapter.Create();
         
            party = engine.GetComp<Party>();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            party = null;
        }
    }
}
