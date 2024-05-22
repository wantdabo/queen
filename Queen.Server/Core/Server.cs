using Queen.Common.Database;
using Queen.Network;
using Sys = Queen.Server.System.Common.Sys;
using Queen.Network.Remote;
using Queen.Core;
using Queen.Server.Player.Common;

namespace Queen.Server.Core
{
    /// <summary>
    /// Queen.Server 引擎
    /// </summary>
    public class Server : Engine<Server>
    {
        public Settings settings;
        public DBO dbo;
        public RPC rpc;
        public Slave slave;
        public Sys sys;
        public Party party;

        protected override void OnCreate()
        {
            base.OnCreate();
            logger.Log("queen.server initial...");
            // 服务器配置
            settings = AddComp<Settings>();
            settings.Create();

            // 数据库
            dbo = AddComp<DBO>();
            dbo.Settings(settings.dbhost, settings.dbuser, settings.dbpwd, settings.dbname);
            dbo.Create();

            // RPC
            rpc = AddComp<RPC>();
            rpc.Create();

            // 网络
            slave = AddComp<Slave>();
            slave.Initialize(settings.host, settings.port, settings.maxconn);
            slave.Create();

            // 系统
            sys = AddComp<Sys>();
            sys.Create();

            // 角色
            party = AddComp<Party>();
            party.Create();

            engine.logger.Log(
                $"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}\n\tmaxconn: {settings.maxconn}" +
                $"\n\tdbhost: {settings.dbhost}\n\tdbname: {settings.dbname}\n\tdbuser: {settings.dbuser}\n\tdbpwd: {settings.dbpwd}\n\tdbsave: {settings.dbsave}"
            , ConsoleColor.Yellow);
            engine.logger.Log("queen.server is running...", ConsoleColor.Green);

            Console.Title = settings.name;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
