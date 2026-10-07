using Aggregates.Testing;
using FluentAssertions;

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
}
