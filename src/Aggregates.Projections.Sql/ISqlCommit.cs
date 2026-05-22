using System.Data;

namespace Aggregates.Projections.Sql;

/// <summary>
/// An <see cref="ICommit"/> that accumulates SQL statements and executes them in a single
/// database transaction when <see cref="ICommit.CommitAsync"/> is called.
/// </summary>
/// <remarks>
/// Use <see cref="ICommitExtensions.UseSql"/> to obtain an <see cref="ISqlCommit"/> from an
/// existing <see cref="ICommit"/> chain. Each call to <see cref="Query"/> returns a new
/// <see cref="ISqlCommit"/> with the statement appended — the original is not modified.
/// </remarks>
public interface ISqlCommit : ICommit {
    /// <summary>
    /// Appends a SQL statement to this commit.
    /// </summary>
    /// <param name="sql">The SQL statement or stored procedure name.</param>
    /// <param name="parameters">
    /// An object whose properties are mapped to query parameters (Dapper-style anonymous objects
    /// are supported). Pass <see langword="null"/> for parameterless statements.
    /// </param>
    /// <param name="commandType">
    /// <see cref="CommandType.Text"/> (default) for inline SQL, or
    /// <see cref="CommandType.StoredProcedure"/> for stored procedure calls.
    /// </param>
    ISqlCommit Query(string sql, object? parameters = null, CommandType commandType = CommandType.Text);
}
