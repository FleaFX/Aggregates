using System.Reflection;

namespace Aggregates.Projections;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddProjections"/>.
/// </summary>
public sealed class ProjectionsOptions {
    internal List<Type> Types { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="IProjection{TEvent}"/> implementations
    /// and automatically registers a handler for each.
    /// </summary>
    public ProjectionsOptions ScanAssemblies(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            Types.AddRange(assembly.GetTypes());
        return this;
    }

    /// <summary>
    /// Inspects <paramref name="types"/> for <see cref="IProjection{TEvent}"/> implementations
    /// and automatically registers a handler for each.
    /// Use this instead of <see cref="ScanAssemblies"/> to register an explicit set of projections.
    /// </summary>
    public ProjectionsOptions ScanTypes(params Type[] types) {
        Types.AddRange(types);
        return this;
    }
}
