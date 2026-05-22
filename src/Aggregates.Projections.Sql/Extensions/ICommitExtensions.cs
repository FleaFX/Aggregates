using System.Data;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Extension methods for attaching SQL commit capabilities to an <see cref="ICommit"/> chain.
/// </summary>
public static class ICommitExtensions {
    /// <summary>
    /// Wraps <paramref name="commit"/> in a new <see cref="ISqlCommit"/> that executes all
    /// accumulated SQL statements in a single database transaction after committing the parent.
    /// </summary>
    /// <param name="commit">The parent commit to run before the SQL transaction.</param>
    /// <param name="connectionFactory">Provides the database connection.</param>
    /// <param name="isolationLevel">
    /// The transaction isolation level. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.
    /// </param>
    public static ISqlCommit UseSql(
        this ICommit commit,
        IDbConnectionFactory connectionFactory,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
        new SqlCommit(commit, connectionFactory, isolationLevel);
}
