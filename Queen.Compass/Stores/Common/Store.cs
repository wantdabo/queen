using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Queen.Compass.Core;
using Queen.Network.Cross;

namespace Queen.Compass.Stores.Common
{
    /// <summary>
    ///     第三方数据
    /// </summary>
    public class Store : Comp
    {
        private Dictionary<string, PointInfo> pointDict = new();
        private Dictionary<string, RoleInfo> roleDict = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.rpc.Routing(RouteDef.GET_POINT, OnGetPoint);
            engine.rpc.Routing(RouteDef.SET_POINT, OnSetPoint);
            engine.rpc.Routing(RouteDef.GET_ROLE, OnGetRole);
            engine.rpc.Routing(RouteDef.SET_ROLE, OnSetRole);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.rpc.UnRouting(RouteDef.GET_POINT, OnGetPoint);
            engine.rpc.UnRouting(RouteDef.SET_POINT, OnSetPoint);
            engine.rpc.UnRouting(RouteDef.GET_ROLE, OnGetRole);
            engine.rpc.UnRouting(RouteDef.SET_ROLE, OnSetRole);
        }

        private void OnGetPoint(CrossContext context)
        {
            var content = JObject.Parse(context.content);
            var name = content.Value<string>("name");
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
            var content = JObject.Parse(context.content);
            var uuid = content.Value<string>("uuid");
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
