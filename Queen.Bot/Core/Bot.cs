﻿using Queen.Common.Parallel;
using Queen.Common.Parallel.Instructions;
using Queen.Core;
using Queen.Network.Common;
using Queen.Network.Cross;
using Queen.Protocols;
using Queen.Protocols.Common;
using System.Collections;
using System.Net;

namespace Queen.Bot.Core;

/// <summary>
/// Queen.Bot 引擎
/// </summary>
public class Bot : Engine<Bot>
{
    /// <summary>
    /// 机器人配置
    /// </summary>
    public Settings settings { get; private set; }
    /// <summary>
    /// 协程
    /// </summary>
    public CoroutineScheduler coroutines { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        settings = AddComp<Settings>();
        settings.Create();

        coroutines = AddComp<CoroutineScheduler>();
        coroutines.Initialize(ticker);
        coroutines.Create();

        engine.logger.Info($"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}", ConsoleColor.Yellow);
        engine.logger.Info("queen.bot is running...");
        Console.Title = settings.name;

        WebSocketTest();
        // var client = AddComp<TCPClient>();
        // client.Initialize(true);
        // client.Create();
        // client.Connect("127.0.0.1", 12801);
        // client.Send(new C2SLoginMsg{username = "", password = ""});
        // client.Recv<S2CLoginMsg>((c, m) =>
        // {
        //     if (1 == m.code)
        //     {
        //         engine.logger.Info("登录成功");
        //         client.Send(new C2STestMsg
        //         {
        //             text = "Hello"
        //         });
        //     }
        //     else
        //     {
        //         engine.logger.Info("登录失败");
        //     }
        // });
    }

    private void WebSocketTest()
    {
        // 创建 WebSocket 服务端
        var wsserv = AddComp<WebSocketServer>();
        wsserv.Initialize("127.0.0.1", 12800, true, 2048, 4, 100);
        wsserv.Create();
        wsserv.Recv((NetChannel c, C2SLoginMsg m) =>
        {
            engine.logger.Info($"username -> {m.username}, password -> {m.password}");
        });

        // 创建 WebSocket 客户端
        var wsclient = AddComp<WebSocketClient>();
        wsclient.Initialize(true);
        wsclient.Create();
        wsclient.Connect("127.0.0.1", 12800);
        
        // 发送消息
        wsclient.Send(new C2SLoginMsg { username = "wantdabo", password = "abc123" });
    }
}
