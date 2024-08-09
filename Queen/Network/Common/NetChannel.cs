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
        private string mid { get; set; }

        /// <summary>
        /// ID
        /// </summary>
        public string id
        {
            get
            {
                return mid;
            }
            private set { mid = value; }
        }

        /// <summary>
        /// 活性
        /// </summary>
        public bool alive { get { return client.Online; } }

        /// <summary>
        /// TS.Client
        /// </summary>
        private TcpSessionClient client { get; set; }

        public NetChannel(TcpSessionClient client)
        {
            this.id = client.Id;
            this.client = client;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (false == alive) return;
            client.SafeCloseAsync();
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
            if (false == alive) return;
            
            client.SendAsync(data);
        }
    }
}
