using Aggregates.Testing;
using FluentAssertions;
using KurrentDB.Client;

namespace Aggregates;

[Trait("Category", "Integration")]
public class RepositoryIntegrationTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Commands_SeeStateOfEarlierCommits(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder), typeof(ShipOrder)));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));
        // Only produces an event when the repository rehydrates Placed...
        await host.SendAsync(new ShipOrder("order-1"));
        // ...and no event when it rehydrates Shipped.
        await host.SendAsync(new ShipOrder("order-1"));

        var stream = (await store.ReadAllAsync()).Where(e => e.Stream == "order-1").ToList();
        stream.Select(e => e.EventType).Should().Equal("IntegrationTests.OrderPlaced@v1", "IntegrationTests.OrderShipped@v1");
        host.Serialization.DeserializeMetadata(stream[0].Metadata).Should().Contain("customer", "alice");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Repository_UpgradesOldEvents_BeforeApply(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(OrderCreated)])
            .Commands(typeof(PlaceOrder), typeof(ShipOrder)));

        await store.AppendAsync(host.Serialization, "order-1", new OrderCreated("order-1"));
        await host.SendAsync(new PlaceOrder("order-1", "alice"));
        await host.SendAsync(new ShipOrder("order-1"));

        var stream = (await store.ReadAllAsync()).Where(e => e.Stream == "order-1").ToList();
        stream.Select(e => e.EventType).Should().Equal("IntegrationTests.OrderCreated@v1", "IntegrationTests.OrderShipped@v1");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Load_FailsOnAnEventTheApplicationDoesNotKnow(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder), typeof(ShipOrder)));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));
        // A stored event type the application doesn't know, like one written before a rename.
        await store.AppendRawAsync("order-1", "IntegrationTests.OrderPlaced@v0", "{}"u8.ToArray());

        Func<Task> act = () => host.SendAsync(new ShipOrder("order-1"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*order-1*revision 1*");
        (await store.ReadAllAsync()).Count(e => e.Stream == "order-1").Should().Be(2, "the failed load commits no event");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Load_FailsOnAnEventOutsideTheAggregatesEvents(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(BillPlanned)])
            .Commands(typeof(PlaceOrder), typeof(ShipOrder)));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));
        // A registered event type, but of another aggregate: it deserializes, just not to an order event.
        await store.AppendAsync(host.Serialization, "order-1", new BillPlanned("order-1", new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero)));

        Func<Task> act = () => host.SendAsync(new ShipOrder("order-1"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*order-1*revision 1*BillPlanned*");
        (await store.ReadAllAsync()).Count(e => e.Stream == "order-1").Should().Be(2, "the failed load commits no event");
    }

    // Truncating a stream is a KurrentDB feature: with $tb, the first event is no longer at revision 0.
    [Fact]
    public async Task Load_FailsOnATruncatedStream() {
        await using var store = (KurrentDbStoreFixture)await Stores.StartAsync(Transport.KurrentDB);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder), typeof(ShipOrder)));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));
        await host.SendAsync(new ShipOrder("order-1"));
        await store.Client.SetStreamMetadataAsync("order-1", StreamState.Any, new StreamMetadata(truncateBefore: new StreamPosition(1)), cancellationToken: TestContext.Current.CancellationToken);

        Func<Task> act = () => host.SendAsync(new ShipOrder("order-1"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*order-1*revision 0*");
    }
}
