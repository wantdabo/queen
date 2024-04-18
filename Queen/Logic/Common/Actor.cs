using Bright.Config;
using Queen.Common;
using Queen.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Logic.Common
{
    public class Actor : Comp
    {
        /// <summary>
        /// 事件订阅派发者
        /// </summary>
        public Eventor eventor;

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

        public T GetBehavior<T>() where T : Behavior
        {
            if (false == behaviorMap.TryGetValue(typeof(T), out var behavior)) return null;

            return behavior as T;
        }

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
