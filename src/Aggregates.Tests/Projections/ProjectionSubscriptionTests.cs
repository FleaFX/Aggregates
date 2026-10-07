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

    [Fact]
    public async Task SurvivesStoreInterruption() {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Projections(typeof(OrderProjection)));
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await Eventually.UntilAsync(() => host.Probe.Count<OrderProjection>() >= 1, "the projection handles order-1");

        await store.InterruptAsync();
        await Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await store.ResumeAsync();
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => host.Probe.Count<OrderProjection>() >= 2 || host.Stopping.IsCompleted,
            "the projection handles order-2 after the interruption", TimeSpan.FromSeconds(60));
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
        host.Probe.Events<OrderProjection>().Should().Equal(new OrderPlaced("order-1", "alice"), new OrderPlaced("order-2", "bob"));
    }

    [ProjectionContract("Orders")]
    sealed class OrderProjection(HandlerProbe probe) : IProjection<IOrderEvent> {
        public ValueTask<ICommit> ProjectAsync(IOrderEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }
}
