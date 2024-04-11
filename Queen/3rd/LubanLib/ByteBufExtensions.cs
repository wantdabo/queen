using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Bright.Serialization
{
    public static class ByteBufExtensions
    {
        public static void WriteUnityVector2(this ByteBuf buf, Vector2 v)
        {
            buf.WriteFloat(v.X);
            buf.WriteFloat(v.Y);
        }

        public static Vector2 ReadUnityVector2(this ByteBuf buf)
        {
            return new Vector2(buf.ReadFloat(), buf.ReadFloat());
        }

        public static void WriteUnityVector3(this ByteBuf buf, Vector3 v)
        {
            buf.WriteFloat(v.X);
            buf.WriteFloat(v.Y);
            buf.WriteFloat(v.Z);
        }

        public static Vector3 ReadUnityVector3(this ByteBuf buf)
        {
            return new Vector3(buf.ReadFloat(), buf.ReadFloat(), buf.ReadFloat());
        }

        public static void WriteUnityVector4(this ByteBuf buf, Vector4 v)
        {
            buf.WriteFloat(v.X);
            buf.WriteFloat(v.Y);
            buf.WriteFloat(v.Z);
            buf.WriteFloat(v.W);
        }

        public static Vector4 ReadUnityVector4(this ByteBuf buf)
        {
            return new Vector4(buf.ReadFloat(), buf.ReadFloat(), buf.ReadFloat(), buf.ReadFloat());
        }
    }
}
