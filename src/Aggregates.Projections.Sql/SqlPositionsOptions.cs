namespace Aggregates.Projections.Sql;

/// <summary>
/// Where <see cref="ServiceCollectionExtensions.RecordSqlPositions"/> records the position of each
/// projected event.
/// </summary>
public sealed class SqlPositionsOptions {
    /// <summary>
    /// The schema of the positions table, created when it doesn't exist. Default: <c>dbo</c>.
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// The name of the positions table, created when it doesn't exist. Default: <c>ProjectionPositions</c>.
    /// </summary>
    public string Table { get; set; } = "ProjectionPositions";
}
