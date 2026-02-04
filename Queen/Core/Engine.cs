using Queen.Common;

namespace Queen.Core;

/// <summary>
/// Engine.Execute 事件
/// </summary>
public struct ExecuteEvent : IEvent
{
}

/// <summary>
/// 引擎组件
/// </summary>
public abstract class Engine : Comp
{
    /// <summary>
    /// 日志
    /// </summary>
    public Logger logger { get; private set; }
    /// <summary>
    /// 事件器
    /// </summary>
    public Eventor eventor { get; private set; }
    /// <summary>
    /// 随机器
    /// </summary>
    public Common.Random random { get; private set; }
    /// <summary>
    /// 调用器
    /// </summary>
    public Caller caller { get; private set; }

    protected override void OnCreate()
    {
        base.OnCreate();

        // 绘制 LOGO
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(
            "\n  ___  _   _ _____ _____ _   _ \n" +
            " / _ \\| | | | ____| ____| \\ | |\n" +
            "| | | | | | |  _| |  _| |  \\| |\n" +
            "| |_| | |_| | |___| |___| |\\  |\n" +
            " \\__\\_\\\\___/|_____|_____|_| \\_|\n\n");

        caller = new Caller();
        SynchronizationContext.SetSynchronizationContext(caller);

        logger = AddComp<Logger>();
        logger.Create();

        eventor = AddComp<Eventor>();
        eventor.Create();

        random = AddComp<Common.Random>();
        random.Create();
    }

    /// <summary>
    /// 引擎运行
    /// </summary>
    public void Run()
    {
        while (true)
        {
            Thread.Sleep(0);
            caller.Pump();
            eventor.Tell<ExecuteEvent>();
        }
    }

    /// <summary>
    /// 创建一个引擎
    /// </summary>
    /// <typeparam name="T">引擎类型</typeparam>
    /// <returns>引擎</returns>
    public static T CreateEngine<T>() where T : Engine, new()
    {
        T engine = new();
        engine.engine = engine;
        engine.Create();

        return engine;
    }
}

/// <summary>
/// 引擎
/// </summary>
/// <typeparam name="T">引擎类型</typeparam>
public abstract class Engine<T> : Engine where T : Engine, new()
{
    public new T engine { get { return base.engine as T; } }
}
