using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Implements <see cref="ISqlCommit"/>. Accumulates SQL statements and executes them in a
/// single ADO.NET transaction when <see cref="CommitAsync"/> is called.
/// </summary>
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

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        try {
            var current = queries;
            while (!current.IsEmpty) {
                current = current.Dequeue(out var query);

                await using var command = connection.CreateCommand();
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

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        } catch {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
