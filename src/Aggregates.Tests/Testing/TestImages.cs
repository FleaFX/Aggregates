namespace Aggregates.Testing;

/// <summary>
/// Pinned container images used by the integration tests. Bumping a tag is a deliberate change.
/// </summary>
static class TestImages {
    /// <summary>
    /// The current KurrentDB LTS release.
    /// </summary>
    public const string KurrentDb = "docker.kurrent.io/kurrent-lts/kurrentdb:26.0.3";

    /// <summary>
    /// SQL Server 2022, for <c>Aggregates.Projections.Sql</c>.
    /// </summary>
    public const string SqlServer = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";
}
