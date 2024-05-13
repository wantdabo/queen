using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Common.Database
{
    /// <summary>
    /// 数据库读取器
    /// </summary>
    public abstract class DBReader
    {
        /// <summary>
        /// 读取器类型
        /// </summary>
        public abstract Type type { get; }

        /// <summary>
        /// 设置属性到实例值
        /// </summary>
        /// <param name="propertyName">属性名</param>
        /// <param name="value">数值</param>
        public void SetPropertyValue(string propertyName, object value)
        {
            var property = type.GetProperty(propertyName);
            if (property != null && value != DBNull.Value)
            {
                property.SetValue(this, Convert.ChangeType(value, property.PropertyType));
            }
        }
    }
}
