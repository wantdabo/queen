using Queen.Network;
using Queen.Remote;
using Queen.Core;
using Queen.Protocols;
using Queen.Common.DB;
using Queen.Server.System.Authentication;
using Queen.Server.System.Commune;

namespace Queen.Server.Core
{
    /// <summary>
    /// Queen.Server 引擎
    /// </summary>
    public class Server : Engine<Server>
    {
        /// <summary>
        /// 服务器配置
        /// </summary>
        public Settings settings { get; private set; }
        /// <summary>
        /// 数据库
        /// </summary>
        public DBO dbo { get; private set; }
        /// <summary>
        /// 网络
        /// </summary>
        public Slave slave { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            settings = AddComp<Settings>();
            settings.Create();

            dbo = AddComp<DBO>();
            dbo.Initialize(settings.dbhost, settings.dbport, settings.dbuser, settings.dbpwd, settings.dbname);
            dbo.Create();

            slave = AddComp<Slave>();
            slave.Initialize(settings.host, settings.port, settings.maxconn);
            slave.Create();

            var authenticator = AddComp<Authenticator>();
            var party = AddComp<Party>();
            authenticator.Create();
            party.Create();

            engine.logger.Info(
                $"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}\n\tmaxconn: {settings.maxconn}" +
                $"\n\tdbhost: {settings.dbhost}\n\tdbport: {settings.dbport}\n\tdbname: {settings.dbname}\n\tdbuser: {settings.dbuser}\n\tdbpwd: {settings.dbpwd}\n\tdbsave: {settings.dbsave}"
                , ConsoleColor.Yellow);
            engine.logger.Info("queen.server is running...");

            Console.Title = settings.name;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
