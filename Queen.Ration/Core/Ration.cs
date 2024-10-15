using Queen.Core;
using Queen.Network.Cross;

namespace Queen.Ration.Core;

public class Ration : Engine<Ration>
{
    /// <summary>
    /// 服务器配置
    /// </summary>
    public Settings settings { get; private set; }
    public RPC rpc { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        settings = AddComp<Settings>();
        settings.Create();
        
        rpc = AddComp<RPC>();
        rpc.Initialize(settings.rpchost, settings.rpcport, settings.rpcidlecc, settings.rpctimeout, settings.rpcdeadtime);
        rpc.Create();

        engine.logger.Info(
            $"\n\tname: {settings.name}"
            , ConsoleColor.Yellow);
        engine.logger.Info("queen.compass is running...");

        Console.Title = settings.name;
    }
}
