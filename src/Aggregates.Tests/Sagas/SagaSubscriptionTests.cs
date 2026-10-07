using System.Runtime.CompilerServices;
using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Sagas;

[Trait("Category", "Integration")]
public class SagaSubscriptionTests(ITestOutputHelper output) {
    [Theory(Skip = KnownIssues.SagaEventCopies), MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SagaDoesNotRetriggerHandlersOfItsEvent(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder))
            .Projections(typeof(PlacedProjection))
            .Policies(typeof(PlacedPolicy))
            .Sagas(s => s
                .ScanTypes(typeof(PlacedSaga))
                .WithResolver<OrderPlaced>(e => [new AggregateIdentifier($"saga-{e.OrderId}")])));

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

    sealed record PlacedState(int Seen) : IState<PlacedState, OrderPlaced> {
        public static PlacedState Initial => new(0);
        public PlacedState Apply(OrderPlaced @event) => this with { Seen = Seen + 1 };
    }

    [SagaContract("Placed", @namespace: "IntegrationTests")]
    sealed class PlacedSaga(HandlerProbe probe) : ISaga<PlacedState, OrderPlaced> {
        public async IAsyncEnumerable<ICommand> ReactAsync(PlacedState state, OrderPlaced @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }

    [ProjectionContract("Placed", @namespace: "IntegrationTests")]
    sealed class PlacedProjection(HandlerProbe probe) : IProjection<OrderPlaced> {
        public ValueTask<ICommit> ProjectAsync(OrderPlaced @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }

    [PolicyContract("Placed", @namespace: "IntegrationTests")]
    sealed class PlacedPolicy(HandlerProbe probe) : IPolicy<OrderPlaced> {
        public async IAsyncEnumerable<ICommand> ReactAsync(OrderPlaced @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }
}
