using System.Data;
using System.Data.Common;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Claims the position of a projected event in the positions table, in the transaction of the
/// commit that writes the event's projection. Registered by
/// <see cref="ServiceCollectionExtensions.RecordSqlPositions"/>.
/// </summary>
sealed class SqlPositions {
    readonly string _claim;

    /// <summary>
    /// Builds the claim for the table of <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The schema and the name of the table.</param>
    internal SqlPositions(SqlPositionsOptions options) {
        Table = $"{Quote(options.Schema)}.{Quote(options.Table)}";
        // One batch, one round trip. UPDLOCK/HOLDLOCK keep the row (or its key range) locked until the
        // transaction ends, so a concurrent claim for the same subscription waits and then finds it taken.
        _claim = $"""
            IF SCHEMA_ID({Literal(options.Schema)}) IS NULL
                EXEC({Literal($"CREATE SCHEMA {Quote(options.Schema)}")});
            IF OBJECT_ID({Literal(Table)}, N'U') IS NULL
                CREATE TABLE {Table} (
                    SubscriptionId nvarchar(200) NOT NULL PRIMARY KEY,
                    Position decimal(20, 0) NOT NULL,
                    Step int NOT NULL);
            UPDATE {Table} WITH (UPDLOCK, HOLDLOCK)
            SET Position = @Position, Step = @Step
            WHERE SubscriptionId = @SubscriptionId
                AND (Position < @Position OR (Position = @Position AND Step < @Step));
            IF @@ROWCOUNT = 1
                SELECT 1;
            ELSE IF EXISTS (SELECT 1 FROM {Table} WITH (UPDLOCK, HOLDLOCK) WHERE SubscriptionId = @SubscriptionId)
                SELECT 0;
            ELSE BEGIN
                INSERT INTO {Table} (SubscriptionId, Position, Step) VALUES (@SubscriptionId, @Position, @Step);
                SELECT 1;
            END
            """;
    }

    /// <summary>
    /// The quoted, schema-qualified name of the positions table.
    /// </summary>
    internal string Table { get; }

    /// <summary>
    /// Claims the position of <paramref name="projected"/> for its next transaction, creating the
    /// schema and the table first when they don't exist.
    /// </summary>
    /// <param name="connection">The open connection of the commit.</param>
    /// <param name="transaction">The transaction of the commit.</param>
    /// <param name="projected">The event being projected.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the transaction should run its statements;
    /// <see langword="false"/> when it was committed before, for an earlier delivery of the event.
    /// </returns>
    internal async ValueTask<bool> ClaimAsync(DbConnection connection, DbTransaction transaction, ProjectedEvent projected, CancellationToken cancellationToken) {
        var query = new Query(_claim, new {
            projected.SubscriptionId,
            // SQL Server has no unsigned 64-bit type; decimal(20, 0) holds every ulong.
            Position = (decimal)projected.Position,
            Step = projected.NextTransaction()
        }, CommandType.Text);
        await using var command = SqlCommit.CreateCommand(connection, transaction, query);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    static string Quote(string name) => $"[{name.Replace("]", "]]")}]";

    static string Literal(string text) => $"N'{text.Replace("'", "''")}'";
}
