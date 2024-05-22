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
        /// 下标
        /// </summary>
        public int index;
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
        /// RPC 名字
        /// </summary>
        public string name { get; private set; }

        /// <summary>
        /// RPC 主机
        /// </summary>
        public string host { get; private set; }

        /// <summary>
        /// RPC 端口
        /// </summary>
        public ushort port { get; private set; }

        /// <summary>
        /// RPC 最大连接数
        /// </summary>
        public int maxconn { get; private set; }

        /// <summary>
        /// RPC 目标
        /// </summary>
        public RPCServInfo[] rpcServs { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/rpc_settings.json"));
            name = jobj.Value<string>("name");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
            maxconn = jobj.Value<int>("maxconn");

            var index = 0;
            var servs = jobj.Value<JArray>("servs");
            rpcServs = new RPCServInfo[servs.Count];
            foreach (var jtoken in servs)
            {
                var serv = new RPCServInfo
                {
                    index = index,
                    name = jtoken.Value<string>("name"),
                    host = jtoken.Value<string>("host"),
                    port = jtoken.Value<ushort>("port")
                };
                rpcServs[index] = serv;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
