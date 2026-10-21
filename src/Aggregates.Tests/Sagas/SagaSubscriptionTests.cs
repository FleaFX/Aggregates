using System.Runtime.CompilerServices;
using System.Text;
using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Sagas;

[Trait("Category", "Integration")]
public class SagaSubscriptionTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SagaDoesNotRetriggerHandlersOfItsEvent(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder))
            .Projections(typeof(PlacedProjection))
            .Policies(typeof(PlacedPolicy))
            .Sagas(s => s.ScanTypes(typeof(PlacedSaga))));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));

        await Eventually.UntilAsync(
            () => host.Probe.Count<PlacedSaga>() >= 1 && host.Probe.Count<PlacedProjection>() >= 1 && host.Probe.Count<PlacedPolicy>() >= 1,
            "the saga, projection and policy handle OrderPlaced");
        await Eventually.QuietAsync();
        host.Probe.Count<PlacedSaga>().Should().Be(1);
        host.Probe.Count<PlacedProjection>().Should().Be(1);
        host.Probe.Count<PlacedPolicy>().Should().Be(1);
        (await store.ReadAllAsync()).Where(e => e.EventType == "IntegrationTests.OrderPlaced@v1").Should()
            .ContainSingle("the saga does not store a copy of its trigger event")
            .Which.Stream.Should().Be("order-1");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SagaCorrelatesAZeroPointThirtySixEventByItsSagaMetadata(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(ZeroPointThirtySixSaga))));

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"),
            ("Saga", new Dictionary<string, object?> { ["EventType"] = typeof(OrderPlaced).AssemblyQualifiedName, ["SagaId"] = "legacy-saga-1" }));

        await Eventually.UntilAsync(() => host.Probe.Count<ZeroPointThirtySixSaga>() >= 1, "the saga handles the 0.36 event");
        await Eventually.QuietAsync();
        host.Probe.Count<ZeroPointThirtySixSaga>().Should().Be(1);
        var stored = await store.ReadAllAsync();
        stored.Where(e => e.Stream == "legacy-saga-1").Should()
            .ContainSingle("the saga wrote one link to its 0.36 stream");
        var link = stored.Single(e => e.Stream == "legacy-saga-1");
        link.EventType.Should().Be("$>");
        Encoding.UTF8.GetString(link.Data.Span).Should().Be("0@order-1");
    }

    sealed record PlacedState(int Seen) : IState<PlacedState, OrderPlaced> {
        public static PlacedState Initial => new(0);
        public PlacedState Apply(OrderPlaced @event) => this with { Seen = Seen + 1 };
    }

    [SagaContract("PlacedSaga", @namespace: "IntegrationTests")]
    sealed class PlacedSaga(HandlerProbe probe) : ISaga<PlacedState, OrderPlaced> {
        public IEnumerable<AggregateIdentifier> Correlate(OrderPlaced @event, EventMetadata metadata) =>
            [new AggregateIdentifier($"saga-{@event.OrderId}")];

        public async IAsyncEnumerable<ICommand> ReactAsync(PlacedState state, OrderPlaced @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }

    [SagaContract("ZeroPointThirtySixSaga", @namespace: "IntegrationTests")]
    sealed class ZeroPointThirtySixSaga(HandlerProbe probe) : ISaga<PlacedState, OrderPlaced> {
        public IEnumerable<AggregateIdentifier> Correlate(OrderPlaced @event, EventMetadata metadata) =>
            metadata.TryGetValue("Saga", out var value)
                ? (value is object?[] array ? array : [value])
                    .OfType<IReadOnlyDictionary<string, object?>>()
                    .Where(saga => saga.TryGetValue("EventType", out var eventType)
                        && eventType is string name && name == typeof(OrderPlaced).AssemblyQualifiedName)
                    .Select(saga => new AggregateIdentifier((string)saga["SagaId"]!))
                : [];

        public async IAsyncEnumerable<ICommand> ReactAsync(PlacedState state, OrderPlaced @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }

    [ProjectionContract("PlacedProjection", @namespace: "IntegrationTests")]
    sealed class PlacedProjection(HandlerProbe probe) : IProjection<OrderPlaced> {
        public ValueTask<ICommit> ProjectAsync(OrderPlaced @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }

    [PolicyContract("PlacedPolicy", @namespace: "IntegrationTests")]
    sealed class PlacedPolicy(HandlerProbe probe) : IPolicy<OrderPlaced> {
        public async IAsyncEnumerable<ICommand> ReactAsync(OrderPlaced @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }
}
