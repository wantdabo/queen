using MessagePack;
using MongoDB.Bson;
using MongoDB.Driver;
using Queen.Common.DB;
using Queen.DBObserve.Core;
using Queen.Server.Roles.Bags;
using Queen.Server.Roles.Common;
using System.Reflection;
using TouchSocket.Core;

namespace Queen.DBObserve.Inputs;

public class InstructionResolver : Comp
{
    private Dictionary<string, Func<string[], (bool, string)>> resolvefuncs = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        resolvefuncs.Add(InstructionDef.HELP, OnHelp);
        resolvefuncs.Add(InstructionDef.FIND, OnFind);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        resolvefuncs.Clear();
    }

    private List<string> temps = new();
    public void Execute(string instruction)
    {
        var arrs = instruction.Split(' ');
        var code = arrs[0];

        if (code.ToUpper().Equals(InstructionDef.CLEAR))
        {
            Console.Clear();
            return;
        }

        if (false == resolvefuncs.TryGetValue(code.ToUpper(), out var func))
        {
            engine.logger.Error($"指令 '{code}' 不存在");
            return;
        }
            
        temps.Clear();
        if (arrs.Length > 1)
            for (int i = 1; i < arrs.Length; i++)
                temps.Add(arrs[i]);

        var result = func.Invoke(temps.ToArray());
        if (false == result.Item1)
        {
            engine.logger.Error(result.Item2);
            return;
        }

        engine.logger.Info($"{result.Item2}");
    }

    private (bool, string) OnHelp(string[] args)
    {
        string content = "    啥也没有";

        return (true, content);
    }

    private (bool, string) OnFind(string[] args)
    {
        var token = args[0];
        var username = args[1].Replace("'", "");
        if (false == engine.dbo.Find("roles", Builders<DBRoleValue>.Filter.Eq(p => p.username, username), out var roles))
        {
            return (false, "not found");
        }

        var role = roles.First();

        if (false == engine.dbo.Find("datas", Builders<DBDataValue>.Filter.Eq(p => p.prefix, $"{token}.{role.uuid}"), out var datas)) return (false, "");
        var data = datas.First();
        var ass = Assembly.Load("Queen.Server");
        var types = ass.GetTypes();
        Type dataType = null;
        foreach (var type in types)
        {
            if (false == typeof(RoleBehavior).IsAssignableFrom(type)) continue;
            var property = type.GetProperty("token");
            if (null == property) continue;
            var ins = ass.CreateInstance(type.ToString());
            if (null == ins) continue;
            var insTokenProperty = type.GetProperty("token");
            var insToken = insTokenProperty.GetValue(ins).ToString();
            if (false == insToken.Equals(token)) continue;
            var insDataProperty = type.GetProperty("data");
            if (null == insDataProperty) continue;
            dataType = insDataProperty.PropertyType;
        }

        if (null == dataType)
        {
            return (true, $"\n{data.value}");
        }
            
        var dv = MessagePackSerializer.Deserialize(dataType, data.value);
        var content = dv.ToJsonString();
        return (true, $"\n{content}");
    }
}