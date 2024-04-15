using Queen.Network.Common;
using Queen.Network.Protocols;
using Queen.Network.Protocols.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Queen.Network.Controller.Common
{
    public class NodeMessageCenter : IDisposable
    {
        private NetNode netNode;
        private Dictionary<Type, List<Action<NetChannel, object>>> messageActionMap = new();
        private List<NodeMessageController> controllers = new();

        public NodeMessageCenter(NetNode node)
        {
            netNode = node;
            node.OnConnect += OnConnect;
            node.OnDisconnect += OnDisconnect;
            node.OnTimeout += OnTimeout;
            node.OnReceive += OnReceive;
        }

        public void Dispose()
        {
            netNode.OnConnect -= OnConnect;
            netNode.OnDisconnect -= OnDisconnect;
            netNode.OnTimeout -= OnTimeout;
            netNode.OnReceive -= OnReceive;
        }

        private void OnConnect(NetChannel channel) 
        {
            Notify(channel, new NodeConnectMessage { });
        }

        private void OnDisconnect(NetChannel channel)
        {
            Notify(channel, new NodeDisconnectMessage { });
        }

        private void OnTimeout(NetChannel channel)
        {
            Notify(channel, new NodeTimeoutMessage { });
        }

        private void OnReceive(NetChannel channel, byte[] data)
        {
            if (false == ProtoPack.UnPack(data, out var msg)) return;
            Notify(channel, msg);
        }

        private void Notify(NetChannel channel, object msg) 
        {
            if (false == messageActionMap.TryGetValue(msg.GetType(), out var actions)) return;
            if (null == actions) return;
            foreach (var action in actions) action?.Invoke(channel, msg);
        }

        public void UnRegisterMessageController(NodeMessageController controller) 
        {
            if (false == controllers.Contains(controller)) return;

            UnListen(controller.mt, controller.Receive);
        }

        public NodeMessageController RegisterMessageController<T>() where T : NodeMessageController, new()
        {
            T controller = new();
            controllers.Add(controller);
            Listen(controller.mt, controller.Receive);

            return controller;
        }

        private void UnListen(Type msgType, Action<NetChannel, object> action)
        {
            if (false == messageActionMap.TryGetValue(msgType, out var actions)) return;
            if (false == actions.Contains(action)) return;

            actions.Remove(action);
        }

        private void Listen(Type msgType, Action<NetChannel, object> action)
        {
            if (false == messageActionMap.TryGetValue(msgType, out var actions))
            {
                actions = new();
                messageActionMap.Add(msgType, actions);
            }

            if (actions.Contains(action)) return;
            actions.Add(action);
        }
    }
}
