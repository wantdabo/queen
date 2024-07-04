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
    /// <typeparam name="T">桥接类型</typeparam>
    public abstract class Adapter<T> : Comp where T : Comp 
    {
        /// <summary>
        /// 桥接
        /// </summary>
        public T bridge;

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
        /// <param name="gridge">桥接</param>
        public void Initialize(T gridge)
        {
            this.bridge = gridge;
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
}
