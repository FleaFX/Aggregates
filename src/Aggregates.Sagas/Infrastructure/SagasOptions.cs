using System.Reflection;

namespace Aggregates.Sagas;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddSagas"/>.
/// </summary>
public sealed class SagasOptions {
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementations and automatically registers a handler for each.
    /// </summary>
    public SagasOptions ScanAssemblies(params Assembly[] assemblies) {
        Assemblies.AddRange(assemblies);
        return this;
    }
}
