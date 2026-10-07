using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Projections;

[Trait("Category", "Integration")]
public class ProjectionSubscriptionTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ProjectsAppendedEvent_Once(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder))
            .Projections(typeof(OrderProjection)));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));

        await Eventually.UntilAsync(() => host.Probe.Count<OrderProjection>() >= 1, "the projection handles OrderPlaced");
        await Eventually.QuietAsync();
        host.Probe.Events<OrderProjection>().Should().Equal(new OrderPlaced("order-1", "alice"));
        host.Probe.Metadata<OrderProjection>().Single().Should().Contain("customer", "alice");
        (await store.ReadAllAsync()).Should().ContainSingle(e => e.EventType == "IntegrationTests.OrderPlaced@v1")
            .Which.Stream.Should().Be("order-1");
    }

    [ProjectionContract("Orders")]
    sealed class OrderProjection(HandlerProbe probe) : IProjection<IOrderEvent> {
        public ValueTask<ICommit> ProjectAsync(IOrderEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }
}
