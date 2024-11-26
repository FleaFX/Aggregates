// ReSharper disable CheckNamespace

using System.Data;
using Aggregates.Projections;

namespace Aggregates.Sql;

public static class ExtensionsForAggregatesOptions {
    /// <summary>
    /// Creates a <see cref="ISqlCommit"/> to use when projecting to SQL.
    /// </summary>
    /// <param name="commit">The originating state, to be returned after committing the changes.</param>
    /// <param name="dbConnectionFactory">The <see cref="IDbConnectionFactory"/> to use when creating a connection to the database.</param>
    /// <param name="isolationLevel">The transaction locking behaviour to use.</param>
    /// <returns>A <see cref="ISqlCommit"/>.</returns>
    public static ICommit UseSql(this ICommit commit, IDbConnectionFactory dbConnectionFactory, IsolationLevel isolationLevel = IsolationLevel.Unspecified) =>
         commit.Use(() => new SqlCommit(dbConnectionFactory, isolationLevel));
}