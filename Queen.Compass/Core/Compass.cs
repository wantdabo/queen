using Queen.Common.MDB;
using Queen.Core;
using Queen.Network.Cross;

namespace Queen.Compass.Core;

public class Compass : Engine<Compass>
{
    /// <summary>
    /// 服务器配置
    /// </summary>
    public Settings settings { get; private set; }
    /// <summary>
    /// RPC
    /// </summary>
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
            $"\n\tname: {settings.name}\n\trpchost: {settings.rpchost}\n\trpcport: {settings.rpcport}\n\trpctimeout: {settings.rpctimeout}\n\trpcdeadtime: {settings.rpcdeadtime}"
        , ConsoleColor.Yellow);
        engine.logger.Info("queen.compass is running...");

        Console.Title = settings.name;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}