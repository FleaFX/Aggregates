using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Extension methods for configuring <c>Aggregates.Projections.Sql</c> on an
/// <see cref="IProjectionsBuilder"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Records the position of each projected event in a table, in the same transaction as the
    /// statements of every <see cref="ISqlCommit"/>, so an event delivered again after a crash has
    /// no effect twice in SQL.
    /// </summary>
    /// <remarks>
    /// Each transaction of an <see cref="ISqlCommit"/> first claims the event's position, and its
    /// place among the event's transactions, for the projection's subscription. When that was
    /// claimed before, the transaction skips its statements. The schema and the table are created
    /// in each database on first use, which needs permission to create them. Other writes in the
    /// commit, and parked events that are replayed, are still delivered at least once. SQL Server
    /// only. Calling this again replaces the earlier configuration.
    /// </remarks>
    /// <param name="builder">The projections builder.</param>
    /// <param name="configure">Optionally sets the schema and the name of the table.</param>
    /// <exception cref="ArgumentException">The schema or the table name is empty.</exception>
    public static IProjectionsBuilder RecordSqlPositions(this IProjectionsBuilder builder, Action<SqlPositionsOptions>? configure = null) {
        var options = new SqlPositionsOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Schema, nameof(options.Schema));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Table, nameof(options.Table));

        builder.Services.Replace(ServiceDescriptor.Singleton(new SqlPositions(options)));
        return builder;
    }
}
