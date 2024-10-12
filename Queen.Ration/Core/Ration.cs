using Queen.Core;

namespace Queen.Ration.Core;

public class Ration : Engine<Ration>
{
    /// <summary>
    /// 服务器配置
    /// </summary>
    public Settings settings { get; set; }

    protected override void OnCreate()
    {
        base.OnCreate();
        settings = AddComp<Settings>();
        settings.Create();
        
        engine.logger.Info(
            $"\n\tname: {settings.name}"
            , ConsoleColor.Yellow);
        engine.logger.Info("queen.compass is running...");

        Console.Title = settings.name;
    }
}
