using Bright.Config;
using Queen.Common;
using Queen.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Common
{
    /// <summary>
    /// Actor/ Behavior 的载体
    /// </summary>
    public class Actor : Comp
    {
        /// <summary>
        /// 事件订阅派发者
        /// </summary>
        public Eventor eventor;

        /// <summary>
        /// behaviors 集合
        /// </summary>
        private Dictionary<Type, Behavior> behaviorMap = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            eventor = AddComp<Eventor>();
            eventor.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            eventor = null;
            behaviorMap.Clear();
        }

        /// <summary>
        /// 获取 Behavior
        /// </summary>
        /// <typeparam name="T">Behavior 类型</typeparam>
        /// <returns>Behavior 实例</returns>
        public T GetBehavior<T>() where T : Behavior
        {
            if (false == behaviorMap.TryGetValue(typeof(T), out var behavior)) return null;

            return behavior as T;
        }

        /// <summary>
        /// 添加 Behavior
        /// </summary>
        /// <typeparam name="T">Behavior 类型</typeparam>
        /// <returns>Behavior 实例</returns>
        /// <exception cref="Exception">不能添加重复的 Behavior</exception>
        public T AddBehavior<T>() where T : Behavior, new()
        {
            if (behaviorMap.ContainsKey(typeof(T))) throw new Exception("can't add repeat behavior.");

            T behavior = AddComp<T>();
            behavior.actor = this;
            behaviorMap.Add(typeof(T), behavior);

            return behavior;
        }
    }
}
