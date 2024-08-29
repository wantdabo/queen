using Queen.Common.DB;
using Queen.Core;
using Queen.DBObserve.Inputs;

namespace Queen.DBObserve.Core;

/// <summary>
/// DB 数据观察
/// </summary>
public class DBObserve : Engine<DBObserve>
{
    /// <summary>
    /// 数据库
    /// </summary>
    public DBO dbo { get; private set; }
    
    /// <summary>
    /// 输入
    /// </summary>
    public Input input { get; private set; }
    
    /// <summary>
    /// 指令解析
    /// </summary>
    public InstructionResolver resolver { get; private set; }

    protected override void OnCreate()
    {
            base.OnCreate();
            dbo = AddComp<DBO>();
            dbo.Initialize("127.0.0.1", 27017, false, "root", "root", "queen");
            dbo.Create();

            input = AddComp<Input>();
            input.Create();

            resolver = AddComp<InstructionResolver>();
            resolver.Create();
            
            Console.Title = "queen.dbobserve";
        }

    protected override void OnDestroy()
    {
            base.OnDestroy();
        }
}