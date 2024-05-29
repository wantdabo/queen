using Newtonsoft.Json.Linq;
using Queen.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network.Remote
{
    /// <summary>
    /// RPC 远程主机信息
    /// </summary>
    public struct RPCServInfo
    {
        /// <summary>
        /// RPC 名字
        /// </summary>
        public string name;

        /// <summary>
        /// RPC 主机
        /// </summary>
        public string host;

        /// <summary>
        /// RPC 端口
        /// </summary>
        public ushort port;
    }

    /// <summary>
    /// RPC 配置
    /// </summary>
    public class RPCSettings : Comp
    {
        /// <summary>
        /// RPC 目标
        /// </summary>
        public Dictionary<string, RPCServInfo> servs { get; private set; } = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/rpc_settings.json"));
            var jarray = jobj.Value<JArray>("servs");
            foreach (var jtoken in jarray)
            {
                var serv = new RPCServInfo
                {
                    name = jtoken.Value<string>("name"),
                    host = jtoken.Value<string>("host"),
                    port = jtoken.Value<ushort>("port")
                };
                servs.Add(serv.name, serv);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
