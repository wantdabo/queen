using Queen.Core;
using Queen.Network.Common;
using Queen.Network.Cross;
using Queen.Protocols;
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

    protected override void OnCreate()
    {
        base.OnCreate();
        settings = AddComp<Settings>();
        settings.Create();

        engine.logger.Info($"\n\tname: {settings.name}\n\tipaddress: {settings.host}\n\tport: {settings.port}", ConsoleColor.Yellow);
        engine.logger.Info("queen.bot is running...");
        Console.Title = settings.name;
        
        var rpc1 = AddComp<RPC>();
        rpc1.Initialize("127.0.0.1", 8801, 500);
        rpc1.Create();
        rpc1.Routing("test/hello", (context) =>
        {
            engine.logger.Info(context.content);
            var content = "";
            for (int i = 0; i < 10000; i++)
            {
                content += "a";
            }
            context.Response(CrossState.Success, content);
        });

        var rpc2 = AddComp<RPC>();
        rpc2.Initialize("127.0.0.1", 8802, 500);
        rpc2.Create();
        rpc2.Cross("127.0.0.1", 8801, "test/hello", "你好，世界！", (state, content) =>
        {
            if (CrossState.Success == state) engine.logger.Info(content);
        });
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
