using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queen.Common.Database
{
    public abstract class DBReader
    {
        public abstract Type type { get; }

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
