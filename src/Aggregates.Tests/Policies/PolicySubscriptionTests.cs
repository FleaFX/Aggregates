using System.Runtime.CompilerServices;
using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Policies;

[Trait("Category", "Integration")]
public class PolicySubscriptionTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DispatchesCommandsOfReaction_Once(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Commands(typeof(PlaceOrder), typeof(ShipOrder))
            .Policies(typeof(ShipOnPlacePolicy)));

        await host.SendAsync(new PlaceOrder("order-1", "alice"));

        await Eventually.UntilAsync(
            async () => (await store.ReadAllAsync()).Any(e => e.Stream == "order-1" && e.EventType == "IntegrationTests.OrderShipped@v1"),
            "the policy ships the order");
        await Eventually.QuietAsync();
        host.Probe.Events<ShipOnPlacePolicy>().Should().Equal(new OrderPlaced("order-1", "alice"));
        (await store.ReadAllAsync()).Where(e => e.Stream == "order-1").Select(e => e.EventType).Should()
            .Equal("IntegrationTests.OrderPlaced@v1", "IntegrationTests.OrderShipped@v1");
    }

    [PolicyContract("ShipOnPlace", @namespace: "IntegrationTests")]
    sealed class ShipOnPlacePolicy(HandlerProbe probe) : IPolicy<OrderPlaced> {
        public async IAsyncEnumerable<ICommand> ReactAsync(OrderPlaced @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield return new ShipOrder(@event.OrderId);
        }
    }
}
