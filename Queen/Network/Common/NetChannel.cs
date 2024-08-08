using Queen.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Queen.Network.Common
{
    /// <summary>
    /// 通信渠道
    /// </summary>
    public class NetChannel
    {
        /// <summary>
        /// TS.Client
        /// </summary>
        public TcpSessionClient client { get; set; }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            client.Close();
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="msg">数据</param>
        public void Send<T>(T msg) where T : INetMessage
        {
            if (ProtoPack.Pack(msg, out var bytes)) Send(bytes);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">二进制数据</param>
        public void Send(byte[] data)
        {
            client.Send(data);
        }
    }
}
