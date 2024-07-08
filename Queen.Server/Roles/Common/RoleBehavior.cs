using MongoDB.Driver;
using Newtonsoft.Json;
using Queen.Common;
using Queen.Common.DB;
using Queen.Network;
using Queen.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Server.Roles.Common
{
    /// <summary>
    /// Behavior/ 行为
    /// </summary>
    public abstract class RoleBehavior : Comp
    {
        public Role role;

        /// <summary>
        /// 恢复数据
        /// </summary>
        public void Restore()
        {
            OnRestore();
        }

        /// <summary>
        /// 备份数据
        /// </summary>
        public void Backup()
        {
            OnBackup();
        }

        /// <summary>
        /// 恢复数据
        /// </summary>
        protected abstract void OnRestore();
        /// <summary>
        /// 备份数据
        /// </summary>
        protected abstract void OnBackup();
    }

    /// <summary>
    /// 数据存储接口
    /// </summary>
    public interface IDBState { }

    /// <summary>
    /// Behavior/ 行为，可存储数据
    /// </summary>
    /// <typeparam name="TDBState">存储数据的类型</typeparam>
    public abstract class RoleBehavior<TDBState> : RoleBehavior where TDBState : IDBState, new()
    {
        /// <summary>
        /// 数据前缀
        /// </summary>
        public string prefix => $"{token}.{role.info.pid}";
        /// <summary>
        /// 标识
        /// </summary>
        public abstract string token { get; }

        /// <summary>
        /// 数据
        /// </summary>
        public TDBState data { get; private set; }

        /// <summary>
        /// 数据备份
        /// </summary>
        private string backup;

        protected override void OnCreate()
        {
            base.OnCreate();
            role.eventor.Listen<DBSaveEvent>(OnDBSave);
            LoadData();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            role.eventor.UnListen<DBSaveEvent>(OnDBSave);
        }

        /// <summary>
        /// 恢复数据
        /// </summary>
        protected override void OnRestore()
        {   
            if (string.IsNullOrEmpty(backup)) throw new Exception("can't restore the data, because backup is null.");

            data = JsonConvert.DeserializeObject<TDBState>(backup);
        }

        /// <summary>
        /// 备份数据
        /// </summary>
        protected override void OnBackup()
        {
            backup = JsonConvert.SerializeObject(data);
        }

        /// <summary>
        /// 加载数据
        /// </summary>
        private void LoadData()
        {
            if (engine.dbo.Find("datas", Builders<DBDataValue>.Filter.Eq(p => p.prefix, prefix), out var values))
            {
                data = JsonConvert.DeserializeObject<TDBState>(values.First().value);

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
            engine.dbo.Replace("datas", Builders<DBDataValue>.Filter.Eq(p => p.prefix, prefix), new() { prefix = prefix, value = json });
        }

        private void OnDBSave(DBSaveEvent e) { SaveData(); }
    }

    /// <summary>
    /// Behavior/ 行为，可存储数据，消息适配器
    /// </summary>
    /// <typeparam name="TDBState">存储数据的类型</typeparam>
    /// <typeparam name="TAdapter">消息适配器的类型></typeparam>
    public abstract class RoleBehavior<TDBState, TAdapter> : RoleBehavior<TDBState> where TDBState : IDBState, new() where TAdapter : Adapter, new()
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            var adapter = AddComp<TAdapter>();
            adapter.Initialize(this);
            adapter.Create();
        }
    }
}
