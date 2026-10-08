using System.Reflection;

namespace Aggregates.Sagas;

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddSagas"/>.
/// </summary>
public sealed class SagasOptions {
    internal List<Type> Types { get; } = [];
    internal List<(Type EventType, object Resolver)> Resolvers { get; } = [];
    internal List<(Type SagaType, Type EventType, object Resolver)> SagaResolvers { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementations and automatically registers a handler for each.
    /// </summary>
    public SagasOptions ScanAssemblies(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            Types.AddRange(assembly.GetTypes());
        return this;
    }

    /// <summary>
    /// Inspects <paramref name="types"/> for <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementations and automatically registers a handler for each.
    /// Use this instead of <see cref="ScanAssemblies"/> to register an explicit set of sagas.
    /// </summary>
    public SagasOptions ScanTypes(params Type[] types) {
        Types.AddRange(types);
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
    /// The function receives the event only; use this overload when the saga identifier is
    /// derivable from the event body alone.
    /// </summary>
    public SagasOptions WithResolver<TEvent>(Func<TEvent, IEnumerable<AggregateIdentifier>> resolve) =>
        WithResolver(new FuncSagaIdResolver<TEvent>(resolve));

    /// <summary>
    /// Registers a <see cref="FuncSagaIdResolver{TEvent}"/> using the supplied function.
    /// The function receives both the event and its stored metadata; use this overload when
    /// saga identifiers are carried in metadata rather than the event body.
    /// </summary>
    public SagasOptions WithResolver<TEvent>(Func<TEvent, EventMetadata, IEnumerable<AggregateIdentifier>> resolve) =>
        WithResolver(new FuncSagaIdResolver<TEvent>(resolve));

    /// <summary>
    /// Registers an <see cref="ISagaIdResolver{TEvent}"/> for <typeparamref name="TSaga"/> only.
    /// It takes precedence over a resolver registered with <see cref="WithResolver{TEvent}(ISagaIdResolver{TEvent})"/>,
    /// so sagas that react to the same event type can each route it to their own saga instances.
    /// </summary>
    /// <typeparam name="TSaga">
    /// The saga class. It must be one of the sagas passed to <see cref="ScanAssemblies"/> or
    /// <see cref="ScanTypes"/>, and react to <typeparamref name="TEvent"/>.
    /// </typeparam>
    /// <typeparam name="TEvent">The event type of the saga.</typeparam>
    public SagasOptions WithResolver<TSaga, TEvent>(ISagaIdResolver<TEvent> resolver) {
        SagaResolvers.Add((typeof(TSaga), typeof(TEvent), resolver));
        return this;
    }

    /// <summary>
    /// Registers a <see cref="FuncSagaIdResolver{TEvent}"/> for <typeparamref name="TSaga"/> only,
    /// using a function that receives the event.
    /// </summary>
    /// <typeparam name="TSaga">The saga class; see <see cref="WithResolver{TSaga,TEvent}(ISagaIdResolver{TEvent})"/>.</typeparam>
    /// <typeparam name="TEvent">The event type of the saga.</typeparam>
    public SagasOptions WithResolver<TSaga, TEvent>(Func<TEvent, IEnumerable<AggregateIdentifier>> resolve) =>
        WithResolver<TSaga, TEvent>(new FuncSagaIdResolver<TEvent>(resolve));

    /// <summary>
    /// Registers a <see cref="FuncSagaIdResolver{TEvent}"/> for <typeparamref name="TSaga"/> only,
    /// using a function that receives the event and its stored metadata.
    /// </summary>
    /// <typeparam name="TSaga">The saga class; see <see cref="WithResolver{TSaga,TEvent}(ISagaIdResolver{TEvent})"/>.</typeparam>
    /// <typeparam name="TEvent">The event type of the saga.</typeparam>
    public SagasOptions WithResolver<TSaga, TEvent>(Func<TEvent, EventMetadata, IEnumerable<AggregateIdentifier>> resolve) =>
        WithResolver<TSaga, TEvent>(new FuncSagaIdResolver<TEvent>(resolve));
}
