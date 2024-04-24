using Queen.Network.Protocols.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Queen.Network.Common
{
    /// <summary>
    /// 网络节点
    /// </summary>
    public class NetNode
    {
        /// <summary>
        /// 消息包结构
        /// </summary>
        private struct NetPackage
        {
            public NetChannel channel;
            public Type msgType;
            public INetMessage msg;
        }

        /// <summary>
        /// 地址
        /// </summary>
        public string? ip;
        /// <summary>
        /// 端口
        /// </summary>
        public ushort port;
        /// <summary>z
        /// 是否自动通知消息
        /// </summary>
        public bool notify { get; protected set; }

        /// <summary>
        /// 注册消息回调集合
        /// </summary>
        private Dictionary<Type, List<Delegate>> messageActionMap = new();
        /// <summary>
        /// 网络消息包缓存
        /// </summary>
        private ConcurrentQueue<NetPackage> netPackages = new();

        /// <summary>
        /// 注销消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调方法</param>
        public void UnListen<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            if (false == messageActionMap.TryGetValue(typeof(T), out var actions)) return;
            if (false == actions.Contains(action)) return;

            actions.Remove(action);
        }

        /// <summary>
        /// 注册消息接收
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="action">回调方法</param>
        public void Listen<T>(Action<NetChannel, T> action) where T : INetMessage
        {
            if (false == messageActionMap.TryGetValue(typeof(T), out var actions))
            {
                actions = new();
                messageActionMap.Add(typeof(T), actions);
            }

            if (actions.Contains(action)) return;
            actions.Add(action);
        }

        /// <summary>
        /// 消息包入队
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <param name="msgType">消息类型</param>
        /// <param name="msg">消息</param>
        private void EnqueuePackage(NetChannel channel, Type msgType, INetMessage msg)
        {
            netPackages.Enqueue(new NetPackage { channel = channel, msgType = msgType, msg = msg });
        }

        /// <summary>
        /// 连接消息
        /// </summary>
        /// <param name="channel">通信渠道</param>
        protected void EmitConnectEvent(NetChannel channel)
        {
            EnqueuePackage(channel, typeof(NodeConnectMsg), new NodeConnectMsg { });
        }

        /// <summary>
        /// 断开连接消息
        /// </summary>
        /// <param name="channel">通信渠道</param>
        protected void EmitDisconnectEvent(NetChannel channel)
        {
            EnqueuePackage(channel, typeof(NodeDisconnectMsg), new NodeDisconnectMsg { });
        }

        /// <summary>
        /// 超时消息
        /// </summary>
        /// <param name="channel">通信渠道</param>
        protected void EmitTimeoutEvent(NetChannel channel)
        {
            EnqueuePackage(channel, typeof(NodeTimeoutMsg), new NodeTimeoutMsg { });
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="channel">通信渠道</param>
        /// <param name="data">数据二进制</param>
        protected void EmitReceiveEvent(NetChannel channel, byte[] data)
        {
            if (false == ProtoPack.UnPack(data, out var msgType, out var msg)) return;
            if (typeof(NodePingMsg) == msgType)
            {
                channel.Send(data);

                return;
            }

            EnqueuePackage(channel, msgType, msg);
            if (notify) Notify();
        }

        /// <summary>
        /// 消息通知
        /// </summary>
        public void Notify()
        {
            while (netPackages.TryDequeue(out var package))
            {
                if (false == messageActionMap.TryGetValue(package.msgType, out var actions)) return;
                if (null == actions) return;
                for (int i = actions.Count - 1; i >= 0; i--) actions[i].DynamicInvoke(package.channel, package.msg);
            }
        }
    }
}
