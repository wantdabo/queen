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
    /// SQL 指令，传参结构
    /// </summary>
    public struct SQLParamInfo
    {
        /// <summary>
        /// 键
        /// </summary>
        public string key;
        /// <summary>
        /// 值
        /// </summary>
        public object value;
    }

    /// <summary>
    /// 数据库操作员
    /// </summary>
    public class DBO : Comp
    {
        /// <summary>
        /// MYSQL 连接
        /// </summary>
        private MySqlConnection connect;

        protected override void OnCreate()
        {
            base.OnCreate();
            engine.logger.Log("create mysql connect.");
            connect = new MySqlConnection($"Server={Settings.host};User ID={Settings.dbuser};Password={Settings.dbpwd};Database={Settings.dbname}");
            engine.logger.Log("create mysql connect success.");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            connect = null;
        }

        /// <summary>
        /// MYSQL 查询
        /// </summary>
        /// <typeparam name="T">数据读取的类型（需要与 MYSQL 表字段对上）</typeparam>
        /// <param name="sql">SQL 语句</param>
        /// <param name="infos">读取数据</param>
        /// <param name="commandParams">SQL 语句传参</param>
        /// <returns>YES/NO</returns>
        public bool Query<T>(string sql, out List<T>? infos, params SQLParamInfo[] commandParams) where T : DBReader, new()
        {
            var hasRows = false;
            infos = null;
            try
            {
                connect.Open();

                var command = new MySqlCommand(sql, connect);
                if (null != commandParams) foreach (var param in commandParams) command.Parameters.AddWithValue(param.key, param.value);

                var reader = command.ExecuteReader();

                hasRows = reader.HasRows;
                if (hasRows) infos = new(); else infos = null;

                while (reader.Read())
                {
                    var info = new T();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var columnValue = reader.GetFieldValueAsync<object>(i);

                        // 将字段值设置到实体对象中
                        info.SetPropertyValue(columnName, columnValue.Result);
                    }
                    infos.Add(info);
                }

                reader.Close();
                command.Dispose();
            }
            finally
            {
                connect.Close();
            }

            return hasRows;
        }

        /// <summary>
        /// MYSQL 执行
        /// </summary>
        /// <param name="sql">SQL 语句</param>
        /// <param name="commandParams">SQL 语句传参</param>
        /// <returns>YES/NO</returns>
        public bool Execute(string sql, params SQLParamInfo[] commandParams)
        {
            try
            {
                connect.Open();

                var command = new MySqlCommand(sql, connect);
                if (null != commandParams) foreach (var param in commandParams) command.Parameters.AddWithValue(param.key, param.value);

                command.ExecuteNonQuery();
                command.Dispose();
            }
            finally 
            {
                connect.Close();
            }

            return true;
        }
    }
}
