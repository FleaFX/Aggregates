using System.Reflection;

namespace Aggregates.Sagas;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddSagas"/>.
/// </summary>
public sealed class SagasOptions {
    internal List<Type> Types { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementations and registers a handler and a subscription for each.
    /// </summary>
    public SagasOptions ScanAssemblies(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            Types.AddRange(assembly.GetTypes());
        return this;
    }

    /// <summary>
    /// Inspects <paramref name="types"/> for <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementations and registers a handler and a subscription for each.
    /// Use this instead of <see cref="ScanAssemblies"/> to register an explicit set of sagas.
    /// </summary>
    public SagasOptions ScanTypes(params Type[] types) {
        Types.AddRange(types);
        return this;
    }
}
