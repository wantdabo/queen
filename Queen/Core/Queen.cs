using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Core
{
    /// <summary>
    /// Queen 对象，框架基础对象
    /// </summary>
    public abstract class Queen
    {
        /// <summary>
        /// 创建一个 Queen 对象
        /// </summary>
        public virtual void Create()
        {
            OnCreate();
        }

        /// <summary>
        /// 销毁一个 Queen 对象
        /// </summary>
        public virtual void Destroy()
        {
            OnDestroy();
        }

        /// <summary>
        /// 创建 Queen 回调
        /// </summary>
        protected abstract void OnCreate();

        /// <summary>
        /// 销毁 Queen 回调
        /// </summary>
        protected abstract void OnDestroy();
    }
}
