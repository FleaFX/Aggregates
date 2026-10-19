using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Implements <see cref="ISqlCommit"/>. Accumulates SQL statements and executes them in a
/// single ADO.NET transaction when <see cref="CommitAsync"/> is called.
/// </summary>
/// <remarks>
/// With <see cref="ServiceCollectionExtensions.RecordSqlPositions"/>, the transaction of a live
/// projected event (<see cref="ProjectedEvent.Current"/>) first claims the event's position; when
/// it was claimed before, the transaction was committed for an earlier delivery and skips its
/// statements.
/// </remarks>
sealed class SqlCommit(
    ICommit parentCommit,
    IDbConnectionFactory connectionFactory,
    IsolationLevel isolationLevel,
    ImmutableQueue<Query> queries) : ISqlCommit {

    internal SqlCommit(ICommit parentCommit, IDbConnectionFactory connectionFactory, IsolationLevel isolationLevel)
        : this(parentCommit, connectionFactory, isolationLevel, ImmutableQueue<Query>.Empty) { }

    /// <inheritdoc/>
    public ISqlCommit Query(string sql, object? parameters = null, CommandType commandType = CommandType.Text) =>
        new SqlCommit(parentCommit, connectionFactory, isolationLevel,
            queries.Enqueue(new Query(sql, parameters, commandType)));

    /// <inheritdoc/>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default) {
        await parentCommit.CommitAsync(cancellationToken);

        if (queries.IsEmpty) return;

        var projected = ProjectedEvent.Current;
        var positions = projected?.Services.GetService<SqlPositions>();

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        try {
            // Claimed in the same transaction as the statements, so both commit or neither does.
            // Not claimed: committed before; disposing the transaction rolls the claim back.
            if (positions is not null && !await positions.ClaimAsync(connection, transaction, projected!, cancellationToken))
                return;

            var current = queries;
            while (!current.IsEmpty) {
                current = current.Dequeue(out var query);
                await using var command = CreateCommand(connection, transaction, query);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        } catch {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Creates a command for <paramref name="query"/> in <paramref name="transaction"/>, with a
    /// parameter per public property of its parameters object.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the command runs in.</param>
    /// <param name="query">The statement, its parameters and its command type.</param>
    internal static DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, Query query) {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query.Sql;
        command.CommandType = query.CommandType;

        if (query.Parameters is not null) {
            foreach (var property in query.Parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                var parameter = command.CreateParameter();
                parameter.ParameterName = property.Name;
                parameter.Value = property.GetValue(query.Parameters) ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        return command;
    }
}
