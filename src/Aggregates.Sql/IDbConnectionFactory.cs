using System.Data;
using System.Data.Common;

namespace Aggregates.Sql;

/// <summary>
/// Provides an <see cref="IDbConnection"/>.
/// </summary>
public interface IDbConnectionFactory {
    /// <summary>
    /// Creates a new <see cref="IDbConnection"/>.
    /// </summary>
    /// <returns>An <see cref="IDbConnection"/>.</returns>
    DbConnection CreateConnection();
}