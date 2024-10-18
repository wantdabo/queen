using MongoDB.Driver;
using Queen.Common.DB;
using Queen.Core;
using Comp = Queen.DBObserve.Core.Comp;

namespace Queen.DBObserve.Inputs;

/// <summary>
/// 输入组件
/// </summary>
public class Input : Comp
{
    protected override void OnCreate()
    {
        base.OnCreate();
        engine.eventor.Listen<ExecuteEvent>(OnExecute);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.eventor.UnListen<ExecuteEvent>(OnExecute);
    }

    private void OnExecute(ExecuteEvent e)
    {
        RecvInput();
    }

    private void RecvInput()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"mongo@{engine.dbo.dbname}:{engine.dbo.dbhost}> ");
        var instruction = Console.ReadLine();

        if (string.IsNullOrEmpty(instruction)) return;
        engine.resolver.Execute(instruction);
    }
}
