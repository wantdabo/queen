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
        private Node node;
        private Dictionary<Type, List<Action<Channel, object>>> messageActionMap = new();
        private List<NodeMessageController> controllers = new();

        public NodeMessageCenter(Node node)
        {
            this.node = node;
            node.OnConnect += OnConnect;
            node.OnDisconnect += OnDisconnect;
            node.OnTimeout += OnTimeout;
            node.OnReceive += OnReceive;
        }

        public void Dispose()
        {
            node.OnConnect -= OnConnect;
            node.OnDisconnect -= OnDisconnect;
            node.OnTimeout -= OnTimeout;
            node.OnReceive -= OnReceive;
        }

        private void OnConnect(Channel channel) 
        {
            Notify(channel, new NodeConnectMessage { });
        }

        private void OnDisconnect(Channel channel)
        {
            Notify(channel, new NodeDisconnectMessage { });
        }

        private void OnTimeout(Channel channel)
        {
            Notify(channel, new NodeTimeoutMessage { });
        }

        private void OnReceive(Channel channel, byte[] data)
        {
            if (false == ProtoPack.UnPack(data, out var msg)) return;
            Notify(channel, msg);
        }

        private void Notify(Channel channel, object msg) 
        {
            if (false == messageActionMap.TryGetValue(msg.GetType(), out var actions)) return;
            if (null == actions) return;
            foreach (var action in actions) action?.Invoke(channel, msg);
        }

        public void UnHookNodeMessageController(NodeMessageController controller) 
        {
            if (false == controllers.Contains(controller)) return;

            UnListen(controller.msgType, controller.Receive);
        }

        public NodeMessageController HookNodeMessageController<T>(Channel channel = null) where T : NodeMessageController, new()
        {
            T controller = new();
            controller.Create(channel);
            controllers.Add(controller);
            Listen(controller.msgType, controller.Receive);

            return controller;
        }

        private void UnListen(Type msgType, Action<Channel, object> action)
        {
            if (false == messageActionMap.TryGetValue(msgType, out var actions)) return;
            if (false == actions.Contains(action)) return;

            actions.Remove(action);
        }

        private void Listen(Type msgType, Action<Channel, object> action)
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
