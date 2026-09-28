using System.Reflection;

namespace Aggregates.Policies;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddPolicies"/>.
/// </summary>
public sealed class PoliciesOptions {
    internal List<Type> Types { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="IPolicy{TEvent}"/> implementations
    /// and automatically registers a handler for each.
    /// </summary>
    public PoliciesOptions ScanAssemblies(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            Types.AddRange(assembly.GetTypes());
        return this;
    }

    /// <summary>
    /// Inspects <paramref name="types"/> for <see cref="IPolicy{TEvent}"/> implementations
    /// and automatically registers a handler for each.
    /// Use this instead of <see cref="ScanAssemblies"/> to register an explicit set of policies.
    /// </summary>
    public PoliciesOptions ScanTypes(params Type[] types) {
        Types.AddRange(types);
        return this;
    }
}
