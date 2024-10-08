
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using Luban;


namespace Conf;

public sealed partial class ItemInfo : Luban.BeanBase
{
    public ItemInfo(ByteBuf _buf) 
    {
        Id = _buf.ReadInt();
        Type = _buf.ReadInt();
        Stack = _buf.ReadInt();
        Quality = _buf.ReadInt();
        Name = _buf.ReadString();
        Desc = _buf.ReadString();
        Icon = _buf.ReadString();
    }

    public static ItemInfo DeserializeItemInfo(ByteBuf _buf)
    {
        return new Conf.ItemInfo(_buf);
    }

    /// <summary>
    /// ID
    /// </summary>
    public readonly int Id;
    /// <summary>
    /// 类型
    /// </summary>
    public readonly int Type;
    /// <summary>
    /// 堆叠
    /// </summary>
    public readonly int Stack;
    /// <summary>
    /// 品质
    /// </summary>
    public readonly int Quality;
    /// <summary>
    /// 名字
    /// </summary>
    public readonly string Name;
    /// <summary>
    /// 描述
    /// </summary>
    public readonly string Desc;
    /// <summary>
    /// 图标
    /// </summary>
    public readonly string Icon;
   
    public const int __ID__ = 2765291;
    public override int GetTypeId() => __ID__;

    public  void ResolveRef(Tables tables)
    {
    }

    public override string ToString()
    {
        return "{ "
               + "Id:" + Id + ","
               + "Type:" + Type + ","
               + "Stack:" + Stack + ","
               + "Quality:" + Quality + ","
               + "Name:" + Name + ","
               + "Desc:" + Desc + ","
               + "Icon:" + Icon + ","
               + "}";
    }
}