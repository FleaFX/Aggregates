using System.Reflection;

namespace Aggregates.Policies;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddPolicies"/>.
/// </summary>
public sealed class PoliciesOptions {
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="IPolicy{TEvent}"/> implementations
    /// and automatically registers a handler for each.
    /// </summary>
    public PoliciesOptions ScanAssemblies(params Assembly[] assemblies) {
        Assemblies.AddRange(assemblies);
        return this;
    }
}
