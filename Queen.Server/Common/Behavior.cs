using Newtonsoft.Json;
using Queen.Common;
using Queen.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Common
{
    /// <summary>
    /// 数据保存事件
    /// </summary>
    public struct DBSaveEvent : IEvent { }

    /// <summary>
    /// Behavior/ 行为
    /// </summary>
    public abstract class Behavior : Comp
    {
        public Actor actor;
    }

    public interface IDBState { }

    /// <summary>
    /// Behavior/ 行为，可存储数据
    /// </summary>
    /// <typeparam name="TDBState">存储数据的类型</typeparam>
    public abstract class Behavior<TDBState> : Behavior where TDBState : IDBState, new()
    {
        /// <summary>
        /// 存储地址
        /// </summary>
        protected abstract string path { get; }

        /// <summary>
        /// 数据
        /// </summary>
        public TDBState data { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            actor.eventor.Listen<DBSaveEvent>(OnDBSave);
            LoadData();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            actor.eventor.UnListen<DBSaveEvent>(OnDBSave);
            SaveData();
        }

        /// <summary>
        /// 加载数据
        /// </summary>
        private void LoadData()
        {
            if (File.Exists(path))
            {
                data = JsonConvert.DeserializeObject<TDBState>(File.ReadAllText(path));

                return;
            }

            data = new TDBState();
            SaveData();
        }

        /// <summary>
        /// 保存数据
        /// </summary>
        private void SaveData()
        {
            var json = JsonConvert.SerializeObject(data);
            File.WriteAllText(path, json);
        }

        private void OnDBSave(DBSaveEvent e) { SaveData(); }
    }
}
