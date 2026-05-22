using System.Data.Common;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Creates <see cref="DbConnection"/> instances for use inside a projection's commit.
/// </summary>
public interface IDbConnectionFactory {
    /// <summary>
    /// Creates and returns a new, unopened <see cref="DbConnection"/>.
    /// The caller is responsible for opening and disposing the connection.
    /// </summary>
    ValueTask<DbConnection> CreateAsync(CancellationToken cancellationToken = default);
}
