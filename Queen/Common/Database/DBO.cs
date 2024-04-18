using MySqlConnector;
using Queen.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Common.Database
{
    /// <summary>
    /// 数据库操作员
    /// </summary>
    public class DBO : Comp
    {
        private MySqlConnection connect;

        protected override void OnCreate()
        {
            base.OnCreate();
            Connect();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            connect.Close();
            connect = null;
        }

        private void Connect()
        {
            engine.logger.Log("connect mysql.", ConsoleColor.Cyan);
            connect = new MySqlConnection($"Server={engine.cfg.dbHost};User ID={engine.cfg.dbUser};Password={engine.cfg.dbPwd};Database={engine.cfg.dbName}");
            connect.Open();
            engine.logger.Log("connect mysql success.", ConsoleColor.Cyan);
        }

        public bool Query<T>(string sql, out List<T>? infos) where T : DBReader, new()
        {
            if (false == (connect.State == System.Data.ConnectionState.Open)) Connect();
            var command = new MySqlCommand(sql, connect);
            var reader = command.ExecuteReader();

            if (reader.HasRows) infos = new(); else infos = null;

            while (reader.Read())
            {
                var info = new T();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var columnValue = reader.GetFieldValueAsync<object>(i);

                    // 将字段值设置到实体对象中
                    info.SetPropertyValue(columnName, columnValue);
                }
            }

            reader.Close();
            command.Dispose();

            return reader.HasRows;
        }

        public int Execute(string sql)
        {
            if (false == (connect.State == System.Data.ConnectionState.Open)) Connect();

            var command = new MySqlCommand(sql, connect);
            var code = command.ExecuteNonQuery();
            command.Dispose();

            return code;
        }
    }
}
