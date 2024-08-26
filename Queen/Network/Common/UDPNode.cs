using Queen.Network.Common.Channels;
using Queen.Protocols.Common;
using System.Net;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Queen.Network.Common
{
    public class PackageType
    {
        public const byte ACK = 1;
        public const byte AACK = 2;
        public const byte DATA = 3;
    }

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
        /// 最大工作线程
        /// </summary>
        protected int sthread { get; set; }
        /// <summary>
        /// UDP Session
        /// </summary>
        private UdpSession udpsession { get; set; }
        /// <summary>
        /// 包头长度
        /// </summary>
        private readonly int PACKAGE_HEAD_LEN = 5;

        protected override void OnCreate()
        {
            base.OnCreate();
            udpsession = new UdpSession();
            udpsession.Received = (c, e) =>
            {
                string ip = e.EndPoint.GetIP();
                int port = e.EndPoint.GetPort();
                var id = $"{ip}:{port}";
                if (false == GetChannel(id, out var channel))
                {
                    var udpnSocket = new UDPNodeSocket(e.EndPoint, this);
                    channel = new UDPNodeC(udpnSocket);
                    AddChannel(channel);
                }

                EmitReceiveEvent(channel, e.ByteBlock.Memory.ToArray());

                return EasyTask.CompletedTask;
            };
            udpsession.Setup(new TouchSocketConfig()
                .SetBindIPHost(new IPHost(port))
            );
            udpsession.Start();
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
        public void Initialize(string ip, ushort port, bool notify, int sthread, int maxpps)
        {
            this.ip = ip;
            this.port = port;
            this.sthread = sthread;
            Initialize(notify, maxpps);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="ep">远程目标</param>
        /// <param name="msg">数据</param>
        public void Send<T>(EndPoint ep, T msg) where T : INetMessage
        {
            if (false == ProtoPack.Pack(msg, out var bytes)) return;

            Send(ep, bytes);
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="ep">远程目标</param>
        /// <param name="data">二进制数据</param>
        public void Send(EndPoint ep, byte[] data)
        {
            if (null == udpsession) return;

            udpsession.Send(ep, data);
        }
    }
}
