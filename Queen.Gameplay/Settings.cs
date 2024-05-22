using Newtonsoft.Json.Linq;
using Queen.Gameplay.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Gameplay
{
    public class Settings : Comp
    {
        /// <summary>
        /// 服务器名字
        /// </summary>
        public string name { get; private set; }

        /// <summary>
        /// 主机
        /// </summary>
        public string host { get; private set; }

        /// <summary>
        /// 端口
        /// </summary>
        public ushort port { get; private set; }

        /// <summary>
        /// 最大连接数
        /// </summary>
        public int maxconn { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
            name = jobj.Value<string>("name");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
            maxconn = jobj.Value<int>("maxconn");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
