using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Bright.Serialization;
using Newtonsoft.Json.Linq;
using Queen.Core;

namespace Queen.Common
{
    /// <summary>
    /// 游戏配置
    /// </summary>
    public class Config : Comp
    {
        /// <summary>
        /// 配置表定位器
        /// </summary>
        public Tables location;

        /// <summary>
        /// 浮点数转整型的乘法系数（10000 表示 1）
        /// </summary>
        public int Float2Int = 10000;

        /// <summary>
        /// 整型转浮点的乘法系数（10000 表示 1）
        /// </summary>
        public float Int2Float = 0.0001f;

        /// <summary>
        /// 服务器名字
        /// </summary>
        public string servName { get; private set; }

        /// <summary>
        /// 端口
        /// </summary>
        public int servPort { get; private set; }

        /// <summary>
        /// 数据库主机
        /// </summary>
        public string dbHost { get; private set; }

        /// <summary>
        /// 数据库用户名
        /// </summary>
        public string dbUser { get; private set; }

        /// <summary>
        /// 数据库密码
        /// </summary>
        public string dbPwd { get; private set; }

        /// <summary>
        /// 数据库名
        /// </summary>
        public string dbName { get; private set; }

        /// <summary>
        /// 数据落地时间间隔（秒）
        /// </summary>
        public int dbSave { get; private set; }

        /// <summary>
        /// 程序 GC 时间间隔（秒）
        /// </summary>
        public int appGc { get; private set; }

        /// <summary>
        /// GM 模式关启
        /// </summary>
        public bool gmMode { get; private set; }

        /// <summary>
        /// 资源目录
        /// </summary>
        public string resPath { get { return $"{Directory.GetCurrentDirectory()}/Res/"; } }

        /// <summary>
        /// 配置表的名字
        /// </summary>
        private List<string> cfgNames = new()
        {
            "Conf.Gameplay.ParkourInfo",
            "Conf.Gameplay.RoguelikeInfo",
            "Conf.Gameplay.AttributeInfo",
            "Conf.Gameplay.StageInfo",
            "Conf.Gameplay.StageGroupInfo",
            "Conf.Gameplay.HeroInfo",
            "Conf.Gameplay.MonsterInfo",
            "Conf.Gameplay.RoguelikeDropInfo",
            "Conf.Gameplay.SkillInfo",
            "Conf.Gameplay.SkillComposeInfo",
            "Conf.Gameplay.EvolutionOptionInfo",
            "Conf.Gameplay.ItemInfo",
            "Conf.Gameplay.EquipInfo",
            "Conf.Gameplay.EquipQualityInfo",
            "Conf.Gameplay.EquipAttributeInfo",
            "Conf.Gameplay.EquipAttributeWeightInfo",
        };

        /// <summary>
        /// 预加载所有配置的 bytes
        /// </summary>
        private Dictionary<string, byte[]> cfgBytesDict = new();

        /// <summary>
        /// 初始化配置表
        /// </summary>
        /// <returns>Task</returns>
        public void Initial()
        {
            var jobj = JObject.Parse(File.ReadAllText($"{resPath}settings.json"));
            servName = jobj.Value<string>("name");
            dbHost = jobj.Value<string>("dbhost");
            dbUser = jobj.Value<string>("dbuser");
            dbPwd = jobj.Value<string>("dbpwd");
            dbName = jobj.Value<string>("dbname");
            dbSave = jobj.Value<int>("dbsave");
            appGc = jobj.Value<int>("appgc");
            gmMode = jobj.Value<bool>("gmmode");

            var path = Directory.GetCurrentDirectory();
            foreach (var cfgName in cfgNames)
            {
                var bytes = File.ReadAllBytes($"{path}/Res/Configs/{cfgName}.bytes");
                cfgBytesDict.Add(cfgName, bytes);
            }
            location = new Tables((cfgName) => new ByteBuf(cfgBytesDict[cfgName]));
        }
    }
}