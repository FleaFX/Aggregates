using System.ComponentModel;
using System.Data;

namespace Aggregates.Sql.Extensions;

public static class ExtensionsForDataCommon {
    /// <summary>
    /// Create a table valued parameter based on the given entity <see cref="T" />
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="typeName"></param>
    /// <returns></returns>
    public static DataTable ToTableValuedParameter<T>(this IEnumerable<T> source, string typeName) {
        var table = new DataTable(typeName);

        var arrSource = source.ToArray();
        if (!arrSource.Any()) return table;

        var template = arrSource.FirstOrDefault();
        if (template == null) return table;

        var properties = TypeDescriptor.GetProperties(typeof(T));

        foreach (PropertyDescriptor prop in properties)
            table.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);

        foreach (var item in arrSource) {
            var row = table.NewRow();
            foreach (PropertyDescriptor prop in properties) {
                row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
            }
            table.Rows.Add(row);
        }

        return table;
    }
}