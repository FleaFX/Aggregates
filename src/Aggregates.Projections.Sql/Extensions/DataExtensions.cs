using System.Data;
using Microsoft.Data.SqlClient.Server;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Extension methods for constructing SQL Server table-valued parameters.
/// </summary>
public static class DataExtensions {
    /// <summary>
    /// Projects <paramref name="source"/> into a SQL Server table-valued parameter using
    /// <paramref name="map"/> to populate each <see cref="SqlDataRecord"/> row.
    /// </summary>
    /// <typeparam name="T">The element type of the source sequence.</typeparam>
    /// <param name="source">The sequence to convert.</param>
    /// <param name="metadata">
    /// The column metadata for the table type, matching the order of values set in
    /// <paramref name="map"/>.
    /// </param>
    /// <param name="map">
    /// A callback that sets the column values on a <see cref="SqlDataRecord"/> for each element.
    /// </param>
    /// <returns>
    /// An <see cref="IEnumerable{SqlDataRecord}"/> suitable for use as a Dapper parameter value
    /// targeting a SQL Server table-valued parameter column.
    /// </returns>
    public static IEnumerable<SqlDataRecord> ToTableValuedParameter<T>(
        this IEnumerable<T> source,
        SqlMetaData[] metadata,
        Action<SqlDataRecord, T> map) {
        var record = new SqlDataRecord(metadata);
        foreach (var item in source) {
            map(record, item);
            yield return record;
        }
    }
}
