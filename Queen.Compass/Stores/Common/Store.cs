using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Queen.Common;
using Queen.Core;
using Queen.Network.Cross;
using System.Collections.Concurrent;
using Comp = Queen.Compass.Core.Comp;

namespace Queen.Compass.Stores.Common;

/// <summary>
/// 第三方数据
/// </summary>
public class Store : Comp
{
    /// <summary>
    /// 数据刷新定时器 ID
    /// </summary>
    private uint refreshTimingId { get; set; }
    /// <summary>
    /// 离线 RPC 的队列
    /// </summary>
    private ConcurrentQueue<string> offlinerpcs = new();
    /// <summary>
    /// RPC 信息集合
    /// </summary>
    private Dictionary<string, CompassRPCInfo> rpcDict = new();
    /// <summary>
    /// Server 信息集合
    /// </summary>
    private Dictionary<string, CompassServerInfo> serverDict = new();
    /// <summary>
    /// 玩家信息集合
    /// </summary>
    private Dictionary<string, CompassRoleInfo> roleDict = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        engine.rpc.Routing(CompassRouteDef.GET_RPC, OnGetRPC);
        engine.rpc.Routing(CompassRouteDef.SET_RPC, OnSetRPC);
        engine.rpc.Routing(CompassRouteDef.GET_SERVER, OnGetServer);
        engine.rpc.Routing(CompassRouteDef.SET_SERVER, OnSetServer);
        engine.rpc.Routing(CompassRouteDef.GET_ROLE, OnGetRole);
        engine.rpc.Routing(CompassRouteDef.SET_ROLE, OnSetRole);
        engine.rpc.Routing(CompassRouteDef.DEL_ROLE, OnDelRole);
        RefreshTiming();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.rpc.UnRouting(CompassRouteDef.GET_RPC, OnGetRPC);
        engine.rpc.UnRouting(CompassRouteDef.SET_RPC, OnSetRPC);
        engine.rpc.UnRouting(CompassRouteDef.GET_SERVER, OnGetServer);
        engine.rpc.UnRouting(CompassRouteDef.SET_SERVER, OnSetServer);
        engine.rpc.UnRouting(CompassRouteDef.GET_ROLE, OnGetRole);
        engine.rpc.UnRouting(CompassRouteDef.SET_ROLE, OnSetRole);
        engine.rpc.UnRouting(CompassRouteDef.DEL_ROLE, OnDelRole);
        engine.ticker.StopTimer(refreshTimingId);
    }
        
    /// <summary>
    /// 定时刷新检查，RPC 信息是否在线，不在线则加入离线队列
    /// </summary>
    private void RefreshTiming()
    {
        List<string> offlineroles = new();
        refreshTimingId = engine.ticker.Timing((t) =>
        {
            offlineroles.Clear();
            while (offlinerpcs.TryDequeue(out var offlinepoint))
            {
                if (rpcDict.ContainsKey(offlinepoint)) rpcDict.Remove(offlinepoint);
                foreach (var kv in roleDict) if (kv.Value.rpc.Equals(offlinepoint)) offlineroles.Add(kv.Key);
                foreach (string offlinerole in offlineroles) if (roleDict.ContainsKey(offlinerole)) roleDict.Remove(offlinerole);
            }

            List<(string, string, ushort)> endpoint = new();
            foreach (var kv in rpcDict) endpoint.Add((kv.Value.name, kv.Value.host, kv.Value.port));
            Task.Run(() =>
            {
                foreach (var (name, ip, port) in endpoint)
                {
                    if (engine.rpc.Online(ip, port)) continue;
                    offlinerpcs.Enqueue(name);
                }
            });
        }, engine.settings.refreshtime, -1);
    }
        
    /// <summary>
    /// 获取 RPC 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnGetRPC(CrossContext context)
    {
        var name = context.content;
        if (false == rpcDict.TryGetValue(name, out var point))
        {
            context.Response(CROSS_STATE.ERROR, "POINT_NOT_FOUND");

            return;
        }

        context.Response(CROSS_STATE.SUCCESS, point);
    }
        
    /// <summary>
    /// 设置 RPC 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnSetRPC(CrossContext context)
    {
        var point = JsonConvert.DeserializeObject<CompassRPCInfo>(context.content);
        if (rpcDict.ContainsKey(point.name)) rpcDict.Remove(point.name);
        rpcDict.Add(point.name, point);

        context.Response(CROSS_STATE.SUCCESS, "OK");
    }
        
    /// <summary>
    /// 获取 Server 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnGetServer(CrossContext context)
    {
        var name = context.content;
        if (false == serverDict.TryGetValue(name, out var server))
        {
            context.Response(CROSS_STATE.ERROR, "SERVER_NOT_FOUND");

            return;
        }

        context.Response(CROSS_STATE.SUCCESS, server);
    }
        
    /// <summary>
    /// 设置 Server 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnSetServer(CrossContext context)
    {
        var server = JsonConvert.DeserializeObject<CompassServerInfo>(context.content);
        if (serverDict.ContainsKey(server.name)) serverDict.Remove(server.name);
        serverDict.Add(server.name, server);

        context.Response(CROSS_STATE.SUCCESS, "OK");
    }
        
    /// <summary>
    /// 获取 Role 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnGetRole(CrossContext context)
    {
        var uuid = context.content;
        if (false == roleDict.TryGetValue(uuid, out var role))
        {
            context.Response(CROSS_STATE.ERROR, "ROLE_NOT_FOUND");

            return;
        }

        context.Response(CROSS_STATE.SUCCESS, role);
    }
        
    /// <summary>
    /// 设置 Role 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnSetRole(CrossContext context)
    {
        var role = JsonConvert.DeserializeObject<CompassRoleInfo>(context.content);
        if (roleDict.ContainsKey(role.uuid)) roleDict.Remove(role.uuid);
        roleDict.Add(role.uuid, role);

        context.Response(CROSS_STATE.SUCCESS, "OK");
    }
        
    /// <summary>
    /// 删除 Role 信息
    /// </summary>
    /// <param name="context">RPC 上下文</param>
    private void OnDelRole(CrossContext context)
    {
        var uuid = context.content;
        if (false == roleDict.TryGetValue(uuid, out var role))
        {
            context.Response(CROSS_STATE.ERROR, "ROLE_NOT_FOUND");

            return;
        }

        roleDict.Remove(uuid);
        context.Response(CROSS_STATE.SUCCESS, "OK");
    }
}