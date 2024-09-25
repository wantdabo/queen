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
        private Dictionary<string, PointInfo> pointDict = new();
        private Dictionary<string, RoleInfo> roleDict = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.rpc.Routing(RouteDef.GET_POINT, OnGetPoint);
            engine.rpc.Routing(RouteDef.SET_POINT, OnSetPoint);
            engine.rpc.Routing(RouteDef.GET_ROLE, OnGetRole);
            engine.rpc.Routing(RouteDef.SET_ROLE, OnSetRole);
            RefreshTiming();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.rpc.UnRouting(RouteDef.GET_POINT, OnGetPoint);
            engine.rpc.UnRouting(RouteDef.SET_POINT, OnSetPoint);
            engine.rpc.UnRouting(RouteDef.GET_ROLE, OnGetRole);
            engine.rpc.UnRouting(RouteDef.SET_ROLE, OnSetRole);
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
                    if (pointDict.ContainsKey(offlinepoint)) pointDict.Remove(offlinepoint);
                    foreach (var kv in roleDict) if (kv.Value.point.Equals(offlinepoint)) offlineroles.Add(kv.Key);
                    foreach (string offlinerole in offlineroles) if (roleDict.ContainsKey(offlinerole)) roleDict.Remove(offlinerole);
                }

                List<(string, string, ushort)> endpoint = new();
                foreach (var kv in pointDict) endpoint.Add((kv.Value.name, kv.Value.ip, kv.Value.port));
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

        private void OnGetPoint(CrossContext context)
        {
            var name = context.content;
            if (false == pointDict.TryGetValue(name, out var point))
            {
                context.Response(CROSS_STATE.ERROR, "POINT_NOT_FOUND");

                return;
            }

            context.Response(CROSS_STATE.SUCCESS, point);
        }

        private void OnSetPoint(CrossContext context)
        {
            engine.logger.Info(context.content);
            var point = JsonConvert.DeserializeObject<PointInfo>(context.content);
            if (pointDict.ContainsKey(point.name)) pointDict.Remove(point.name);
            pointDict.Add(point.name, point);

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
            var role = JsonConvert.DeserializeObject<RoleInfo>(context.content);
            if (roleDict.ContainsKey(role.uuid)) roleDict.Remove(role.uuid);
            roleDict.Add(role.uuid, role);

            context.Response(CROSS_STATE.SUCCESS, "OK");
        }
    }
}
