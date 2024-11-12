using Aggregates.Projections;
using System.Collections.Immutable;
using System.Data;
using System.Reflection;

namespace Aggregates.Sql;

/// <summary>
/// Specialization of a <see cref="ICommit{TState}"/> that keeps the changes to a projection as a sequence of queries to execute when committing.
/// </summary>
/// <typeparam name="TState">The type of the maintained projection</typeparam>
public interface ISqlCommit<TState> : ICommit<TState> {
    /// <summary>
    /// Prepares a <see cref="ISqlCommit{TState}"/> that will execute the given query when committed, along with any queries that were previously prepared.
    /// </summary>
    /// <param name="sql">The command text to execute.</param>
    /// <param name="parameters">Optional object that captures the parameters of the query. Each property on this object should match the name of the parameter to set.</param>
    /// <param name="commandType">The type of command to execute. Defaults to <c>CommandType.Text</c>.</param>
    /// <returns></returns>
    ISqlCommit<TState> Query(string sql, object? parameters = null, CommandType commandType = CommandType.Text);
}

/// <summary>
/// A prepared projection state that commits the changes to a SQL database.
/// </summary>
/// <param name="Origin">The originating state, to be returned after committing the changes.</param>
/// <param name="DbConnectionFactory">The <see cref="IDbConnectionFactory"/> to use when creating a connection to the database.</param>
/// <param name="IsolationLevel">The transaction locking behaviour.</param>
/// <typeparam name="TState">The type of the state returned after committing.</typeparam>
readonly record struct SqlCommit<TState>(TState Origin, IDbConnectionFactory DbConnectionFactory, IsolationLevel IsolationLevel) : ISqlCommit<TState> {
    ImmutableQueue<Query> UncommittedQueries { get; init; } = ImmutableQueue<Query>.Empty;

    /// <summary>
    /// Prepares a <see cref="ICommit{TState}"/> that will execute the given query when committed, along with any queries that were previously prepared.
    /// </summary>
    /// <param name="sql">The command text to execute.</param>
    /// <param name="parameters">Optional object that captures the parameters of the query. Each property on this object should match the name of the parameter to set.</param>
    /// <param name="commandType">The type of command to execute. Defaults to <c>CommandType.Text</c>.</param>
    /// <returns></returns>
    public ISqlCommit<TState> Query(string sql, object? parameters = null, CommandType commandType = CommandType.Text) =>
        this with { UncommittedQueries = UncommittedQueries.Enqueue(new Query(sql, parameters, commandType)) };

    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    async ValueTask<TState> ICommit<TState>.CommitAsync(CancellationToken cancellationToken) {
        await using var connection = DbConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel, cancellationToken);

        try {
            var uncommittedQueries = UncommittedQueries;
            while (!uncommittedQueries.IsEmpty) {
                uncommittedQueries = uncommittedQueries.Dequeue(out var query);

                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandType = query.CommandType;
                command.CommandText = query.Sql;
                foreach (var property in query.Parameters?.GetType().GetRuntimeProperties() ??
                                         Array.Empty<PropertyInfo>()) {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = property.Name;
                    parameter.Value = property.GetValue(query.Parameters);
                    command.Parameters.Add(parameter);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
        } catch {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return Origin;
    }
}