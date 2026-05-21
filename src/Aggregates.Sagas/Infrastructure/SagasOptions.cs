using System.Reflection;

namespace Aggregates.Sagas;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddSagas"/>.
/// </summary>
public sealed class SagasOptions {
    internal List<Assembly> Assemblies { get; } = [];
    internal List<(Type EventType, object Resolver)> Resolvers { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementations and automatically registers a handler for each.
    /// </summary>
    public SagasOptions ScanAssemblies(params Assembly[] assemblies) {
        Assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Registers an <see cref="ISagaIdResolver{TEvent}"/> that determines which saga instance
    /// an incoming <typeparamref name="TEvent"/> belongs to.
    /// </summary>
    public SagasOptions WithResolver<TEvent>(ISagaIdResolver<TEvent> resolver) {
        Resolvers.Add((typeof(TEvent), resolver));
        return this;
    }

    /// <summary>
    /// Registers a <see cref="FuncSagaIdResolver{TEvent}"/> using the supplied function.
    /// </summary>
    public SagasOptions WithResolver<TEvent>(Func<TEvent, IEnumerable<AggregateIdentifier>> resolve) =>
        WithResolver(new FuncSagaIdResolver<TEvent>(resolve));
}
