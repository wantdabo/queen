using Queen.Network.Common.Channels;
using Queen.Protocols.Common;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Queen.Network.Common
{
    /// <summary>
    /// UDP 网络节点
    /// </summary>
    public class UDPNode : NetNode
    {
        /// <summary>
        /// 地址
        /// </summary>
        public string ip { get; protected set; }
        /// <summary>
        /// 端口
        /// </summary>
        public ushort port { get; protected set; }

        /// <summary>
        /// 通信渠道
        /// </summary>
        private UDPNodeC channel { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            var udpSession = new UdpSession();
            channel = new UDPNodeC(udpSession);
            udpSession.Received = (c, e) =>
            {
                return EasyTask.CompletedTask;
            };
            udpSession.Setup(new TouchSocketConfig()
                .SetBindIPHost(new IPHost(port))
            );
            udpSession.Start();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>
        /// 创建服务端网络节点
        /// </summary>
        /// <param name="ip">地址</param>
        /// <param name="port">端口</param>
        /// <param name="notify">是否自动通知消息（子线程）</param>
        public void Initialize(string ip, ushort port, bool notify)
        {
            this.ip = ip;
            this.port = port;
            Initialize(notify, int.MaxValue);
        }
        
        /// <summary>
        /// 发送数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="msg">数据</param>
        public void Send<T>(T msg) where T : INetMessage
        {
            if (null == channel) return;
            channel.Send(msg);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">二进制数据</param>
        public void Send(byte[] data)
        {
            if (null == channel) return;
            channel.Send(data);
        }
    }
}
