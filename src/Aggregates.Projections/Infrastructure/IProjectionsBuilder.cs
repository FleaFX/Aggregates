using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections;

/// <summary>
/// A builder for configuring the <c>Aggregates.Projections</c> package and its storage integrations.
/// </summary>
public interface IProjectionsBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The projections registered via <see cref="ProjectionsOptions.ScanAssemblies"/>, as
    /// <c>(EventType, ProjectionType)</c> pairs. Used by integration packages to set up
    /// subscription services.
    /// </summary>
    IReadOnlyList<(Type EventType, Type ProjectionType)> RegisteredProjections { get; }
}
