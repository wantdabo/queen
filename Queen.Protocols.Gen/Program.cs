﻿using MessagePack;
using System.Reflection;
using System.Text;

var assembly = Assembly.Load("Queen.Protocols");
var types = assembly.GetTypes();

StringBuilder sb = new StringBuilder();
sb.AppendLine("using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Queen.Protocols.Common\r\n{\r\n    public partial class ProtoPack\r\n    {\r\n        /// <summary>\r\n        /// 协议号定义\r\n        /// </summary>\r\n        private static List<Type> messageIds = new()\r\n        {");
foreach (var type in types)
{
    if (null == type.GetInterface("INetMessage") || null == type.GetCustomAttribute<MessagePackObjectAttribute>()) continue;
    sb.AppendLine($"            typeof({type.FullName}),");
}
sb.AppendLine("        };\r\n    }\r\n}\r\n");
var code = sb.ToString();
File.WriteAllText($"../../../../Queen.Protocols/Common/__PROTO__DEFINE__.cs", code);

Console.WriteLine("Queen.Protocols.Gen Finised.");
Console.ReadKey();