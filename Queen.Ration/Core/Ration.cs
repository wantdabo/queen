using Queen.Core;
using Queen.Network.Cross;

namespace Queen.Ration.Core;

public class Ration : Engine<Ration>
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

        engine.logger.Info($"\n\tname: {settings.name}", ConsoleColor.Yellow);
        engine.logger.Info("queen.ration is running...");

        Console.Title = settings.name;

        StartupWebAPI();
    }
    
    /// <summary>
    /// 启动 WebAPI
    /// </summary>
    private void StartupWebAPI()
    {
        var builder = WebApplication.CreateBuilder();

        // Add services to the container.
        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseAuthorization();

        app.MapControllers();

        app.RunAsync();
    }
}
