using System.Reflection;

namespace Aggregates.Projections;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddProjections"/>.
/// </summary>
public sealed class ProjectionsOptions {
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="IProjection{TEvent}"/> implementations
    /// and automatically registers a handler for each.
    /// </summary>
    public ProjectionsOptions ScanAssemblies(params Assembly[] assemblies) {
        Assemblies.AddRange(assemblies);
        return this;
    }
}
