using Newtonsoft.Json.Linq;
using Queen.Bot.Core;

namespace Queen.Bot
{
    public class Settings : Comp
    {
        /// <summary>
        /// 名字
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

        protected override void OnCreate()
        {
            base.OnCreate();
            var jobj = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/Res/settings.json"));
            name = jobj.Value<string>("name");
            host = jobj.Value<string>("host");
            port = jobj.Value<ushort>("port");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
