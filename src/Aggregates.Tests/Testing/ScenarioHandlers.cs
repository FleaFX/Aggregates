using System.Runtime.CompilerServices;
using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Sagas;

namespace Aggregates.Testing;

/// <summary>
/// The kinds of subscription handlers.
/// </summary>
public enum HandlerKind {
    /// <summary>
    /// An <see cref="IProjection{TEvent}"/>.
    /// </summary>
    Projection,

    /// <summary>
    /// An <see cref="IPolicy{TEvent}"/>.
    /// </summary>
    Policy,

    /// <summary>
    /// An <see cref="ISaga{TSagaState,TEvent}"/>.
    /// </summary>
    Saga
}

/// <summary>
/// Probe handlers of each <see cref="HandlerKind"/> on <see cref="IOrderEvent"/>. They record
/// every event in the <see cref="HandlerProbe"/>, react with no commands, and throw for orders
/// whose id starts with <see cref="PoisonPrefix"/>.
/// </summary>
static class ScenarioHandlers {
    /// <summary>
    /// Events of orders whose id starts with this prefix make every probe handler throw.
    /// </summary>
    public const string PoisonPrefix = "poison";

    /// <summary>
    /// All handler kinds, for <c>[MemberData]</c> combined with <see cref="Stores.All"/>.
    /// </summary>
    public static TheoryData<HandlerKind, Transport> KindsAndTransports {
        get {
            var data = new TheoryData<HandlerKind, Transport>();
            foreach (var kind in Enum.GetValues<HandlerKind>())
            foreach (var transport in Enum.GetValues<Transport>())
                data.Add(kind, transport);
            return data;
        }
    }

    /// <summary>
    /// The probe handler of <paramref name="kind"/>.
    /// </summary>
    public static Type Probe(HandlerKind kind) => kind switch {
        HandlerKind.Projection => typeof(ProbeProjection),
        HandlerKind.Policy => typeof(ProbePolicy),
        _ => typeof(ProbeSaga)
    };

    /// <summary>
    /// A second probe handler of <paramref name="kind"/>, on the same event type as <see cref="Probe"/>.
    /// </summary>
    public static Type SecondProbe(HandlerKind kind) => kind switch {
        HandlerKind.Projection => typeof(SecondProbeProjection),
        HandlerKind.Policy => typeof(SecondProbePolicy),
        _ => typeof(SecondProbeSaga)
    };

    /// <summary>
    /// A probe handler of <paramref name="kind"/> whose contract starts from the end of the store.
    /// </summary>
    public static Type FromEnd(HandlerKind kind) => kind switch {
        HandlerKind.Projection => typeof(FromEndProjection),
        HandlerKind.Policy => typeof(FromEndPolicy),
        _ => typeof(FromEndSaga)
    };

    /// <summary>
    /// Registers <paramref name="handlerTypes"/> as handlers of <paramref name="kind"/>. Each probe
    /// saga gets its own resolver, so two probe sagas never share a saga stream: <see cref="Probe"/>
    /// maps each order to the saga <c>saga-{OrderId}</c>, the others use their own prefix.
    /// </summary>
    public static TestHostOptions Handlers(this TestHostOptions options, HandlerKind kind, params Type[] handlerTypes) => kind switch {
        HandlerKind.Projection => options.Projections(handlerTypes),
        HandlerKind.Policy => options.Policies(handlerTypes),
        _ => options.Sagas(s => handlerTypes.Aggregate(s.ScanTypes(handlerTypes), (sagas, sagaType) => OwnResolvers[sagaType](sagas)))
    };

    static readonly Dictionary<Type, Func<SagasOptions, SagasOptions>> OwnResolvers = new() {
        [typeof(ProbeSaga)] = s => s.WithResolver<ProbeSaga, IOrderEvent>(e => [new AggregateIdentifier($"saga-{e.OrderId}")]),
        [typeof(SecondProbeSaga)] = s => s.WithResolver<SecondProbeSaga, IOrderEvent>(e => [new AggregateIdentifier($"second-saga-{e.OrderId}")]),
        [typeof(FromEndSaga)] = s => s.WithResolver<FromEndSaga, IOrderEvent>(e => [new AggregateIdentifier($"from-end-saga-{e.OrderId}")])
    };

    static void Handle(HandlerProbe probe, object handler, IOrderEvent @event, EventMetadata? metadata = null) {
        if (@event.OrderId.StartsWith(PoisonPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"{handler.GetType().Name} fails on {@event.OrderId} on purpose.");
        probe.Record(handler, @event, metadata);
    }

    abstract class ProjectionBase(HandlerProbe probe) : IProjection<IOrderEvent> {
        public ValueTask<ICommit> ProjectAsync(IOrderEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            Handle(probe, this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }

    abstract class PolicyBase(HandlerProbe probe) : IPolicy<IOrderEvent> {
        public async IAsyncEnumerable<ICommand> ReactAsync(IOrderEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            Handle(probe, this, @event);
            yield break;
        }
    }

    /// <summary>
    /// The state of the probe sagas: the number of events seen.
    /// </summary>
    internal sealed record ProbeSagaState(int Seen) : IState<ProbeSagaState, IOrderEvent> {
        /// <inheritdoc/>
        public static ProbeSagaState Initial => new(0);

        /// <inheritdoc/>
        public ProbeSagaState Apply(IOrderEvent @event) => this with { Seen = Seen + 1 };
    }

    abstract class SagaBase(HandlerProbe probe) : ISaga<ProbeSagaState, IOrderEvent> {
        public async IAsyncEnumerable<ICommand> ReactAsync(ProbeSagaState state, IOrderEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            Handle(probe, this, @event);
            yield break;
        }
    }

    [ProjectionContract("Probe", @namespace: "IntegrationTests.Projections")]
    sealed class ProbeProjection(HandlerProbe probe) : ProjectionBase(probe);

    [ProjectionContract("SecondProbe", @namespace: "IntegrationTests.Projections")]
    sealed class SecondProbeProjection(HandlerProbe probe) : ProjectionBase(probe);

    [ProjectionContract("FromEndProbe", @namespace: "IntegrationTests.Projections", startFromEnd: true)]
    sealed class FromEndProjection(HandlerProbe probe) : ProjectionBase(probe);

    [PolicyContract("Probe", @namespace: "IntegrationTests.Policies")]
    sealed class ProbePolicy(HandlerProbe probe) : PolicyBase(probe);

    [PolicyContract("SecondProbe", @namespace: "IntegrationTests.Policies")]
    sealed class SecondProbePolicy(HandlerProbe probe) : PolicyBase(probe);

    [PolicyContract("FromEndProbe", @namespace: "IntegrationTests.Policies", startFromEnd: true)]
    sealed class FromEndPolicy(HandlerProbe probe) : PolicyBase(probe);

    [SagaContract("Probe", @namespace: "IntegrationTests.Sagas")]
    sealed class ProbeSaga(HandlerProbe probe) : SagaBase(probe);

    [SagaContract("SecondProbe", @namespace: "IntegrationTests.Sagas")]
    sealed class SecondProbeSaga(HandlerProbe probe) : SagaBase(probe);

    [SagaContract("FromEndProbe", @namespace: "IntegrationTests.Sagas", startFromEnd: true)]
    sealed class FromEndSaga(HandlerProbe probe) : SagaBase(probe);
}
