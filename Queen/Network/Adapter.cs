using Queen.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Network
{
    /// <summary>
    /// 消息适配器
    /// </summary>
    public abstract class Adapter : Comp
    {
        /// <summary>
        /// 桥接
        /// </summary>
        protected Comp bridge;

        protected override void OnCreate()
        {
            base.OnCreate();
            OnBind();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnUnbind();
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="bridge">桥接</param>
        public void Initialize(Comp bridge)
        {
            this.bridge = bridge;
        }

        /// <summary>
        /// 绑定消息回调
        /// </summary>
        protected abstract void OnBind();

        /// <summary>
        /// 松绑消息回调
        /// </summary>
        protected abstract void OnUnbind();
    }

    /// <summary>
    /// 消息适配器
    /// </summary>
    /// <typeparam name="T">桥接类型</typeparam>
    public abstract class Adapter<T> : Adapter where T : Comp
    {
        /// <summary>
        /// 桥接
        /// </summary>
        protected new T bridge { get { return base.bridge as T; } }
    }
}
