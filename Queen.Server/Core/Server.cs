﻿using Queen.Network;
using Queen.Remote;
using Queen.Core;
using Queen.Server.System;
using Queen.Protocols;
using Queen.Common.DB;

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
        public Settings settings;
        /// <summary>
        /// 数据库
        /// </summary>
        public DBO dbo;
        /// <summary>
        /// RPC
        /// </summary>
        public RPC rpc;
        /// <summary>
        /// 网络
        /// </summary>
        public Slave slave;

        protected override void OnCreate()
        {
            base.OnCreate();
            logger.Log("queen.server initial...");
            settings = AddComp<Settings>();
            settings.Create();

            dbo = AddComp<DBO>();
            dbo.Initialize(settings.dbhost, settings.dbport, settings.dbuser, settings.dbpwd, settings.dbname);
            dbo.Create();

            rpc = AddComp<RPC>();
            rpc.Initialize(RPC.TAR.SERV);
            rpc.Create();
            
            slave = AddComp<Slave>();
            slave.Initialize(settings.host, settings.port, settings.maxconn);
            slave.Create();

            var login = AddComp<Authenticator>();
            var party = AddComp<Party>();
            login.Create();
            party.Create();

            engine.logger.Log(
                $"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}\n\tmaxconn: {settings.maxconn}" +
                $"\n\tdbhost: {settings.dbhost}\n\tdbport: {settings.dbport}\n\tdbname: {settings.dbname}\n\tdbuser: {settings.dbuser}\n\tdbpwd: {settings.dbpwd}\n\tdbsave: {settings.dbsave}"
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
