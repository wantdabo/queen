﻿using Bright.Config;
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
    /// 数据保存事件
    /// </summary>
    public struct DBSaveEvent : IEvent { }

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
        private Dictionary<Type, Behavior> behaviorDict = new();

        private uint dbsaveTiming;

        protected override void OnCreate()
        {
            base.OnCreate();
            eventor = AddComp<Eventor>();
            eventor.Create();

            // 数据写盘
            dbsaveTiming = engine.ticker.Timing((t) => eventor.Tell<DBSaveEvent>(), engine.settings.dbsave, -1);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            engine.ticker.StopTimer(dbsaveTiming);
            behaviorDict.Clear();
        }

        /// <summary>
        /// 获取 Behavior
        /// </summary>
        /// <typeparam name="T">Behavior 类型</typeparam>
        /// <returns>Behavior 实例</returns>
        public T GetBehavior<T>() where T : Behavior
        {
            if (false == behaviorDict.TryGetValue(typeof(T), out var behavior)) return null;

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
            if (behaviorDict.ContainsKey(typeof(T))) throw new Exception("can't add repeat behavior.");

            T behavior = AddComp<T>();
            behavior.actor = this;
            behaviorDict.Add(typeof(T), behavior);

            return behavior;
        }
    }
}
