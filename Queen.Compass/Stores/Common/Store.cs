using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Queen.Common;
using Queen.Core;
using Queen.Network.Cross;
using System.Collections.Concurrent;
using Comp = Queen.Compass.Core.Comp;

namespace Queen.Compass.Stores.Common
{
    /// <summary>
    /// 第三方数据
    /// </summary>
    public class Store : Comp
    {
        private uint refreshTimingId { get; set; }
        private ConcurrentQueue<string> offlinepoints = new();
        private Dictionary<string, CompassRPCInfo> rpcDict = new();
        private Dictionary<string, CompassServerInfo> serverDict = new();
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

        private void RefreshTiming()
        {
            List<string> offlineroles = new();
            refreshTimingId = engine.ticker.Timing((t) =>
            {
                offlineroles.Clear();
                while (offlinepoints.TryDequeue(out var offlinepoint))
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
                        offlinepoints.Enqueue(name);
                    }
                });
            }, engine.settings.refreshtime, -1);
        }

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

        private void OnSetRPC(CrossContext context)
        {
            var point = JsonConvert.DeserializeObject<CompassRPCInfo>(context.content);
            if (rpcDict.ContainsKey(point.name)) rpcDict.Remove(point.name);
            rpcDict.Add(point.name, point);

            context.Response(CROSS_STATE.SUCCESS, "OK");
        }
        
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
        
        private void OnSetServer(CrossContext context)
        {
            var server = JsonConvert.DeserializeObject<CompassServerInfo>(context.content);
            if (serverDict.ContainsKey(server.name)) serverDict.Remove(server.name);
            serverDict.Add(server.name, server);

            context.Response(CROSS_STATE.SUCCESS, "OK");
        }
        
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

        private void OnSetRole(CrossContext context)
        {
            var role = JsonConvert.DeserializeObject<CompassRoleInfo>(context.content);
            if (roleDict.ContainsKey(role.uuid)) roleDict.Remove(role.uuid);
            roleDict.Add(role.uuid, role);

            context.Response(CROSS_STATE.SUCCESS, "OK");
        }
        
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
}
