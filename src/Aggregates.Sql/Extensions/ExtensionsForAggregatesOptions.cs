// ReSharper disable CheckNamespace

using System.Data;
using Aggregates.Projections;

namespace Aggregates.Sql;

public static class ExtensionsForAggregatesOptions {
    /// <summary>
    /// Creates a <see cref="ISqlCommit"/> to use when projecting to SQL.
    /// </summary>
    /// <param name="state">The originating state, to be returned after committing the changes.</param>
    /// <param name="dbConnectionFactory">The <see cref="IDbConnectionFactory"/> to use when creating a connection to the database.</param>
    /// <param name="isolationLevel">The transaction locking behaviour to use.</param>
    /// <returns>A <see cref="ISqlCommit"/>.</returns>
    public static ISqlCommit UseSql(this ICommit state, IDbConnectionFactory dbConnectionFactory, IsolationLevel isolationLevel = IsolationLevel.Unspecified) =>
        new SqlCommit(dbConnectionFactory, isolationLevel);
}