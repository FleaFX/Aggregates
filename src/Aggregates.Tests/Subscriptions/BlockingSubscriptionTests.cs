using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Subscriptions;

/// <summary>
/// Integration tests for subscriptions that block on a failing event instead of parking it, on both
/// transports: they retry the event until it succeeds and handle nothing else until then.
/// </summary>
[Trait("Category", "Integration")]
public class BlockingSubscriptionTests(ITestOutputHelper output) {
    static readonly TimeSpan PatientTimeout = TimeSpan.FromSeconds(60);

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task BlockingSubscription_RetriesTheFailingEvent_UntilItSucceeds_InOrder(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Blocking(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.QuietAsync();

        host.Probe.Events(handler).Should().BeEmpty("neither event is handled while the first one fails");
        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("parked-"));
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");

        host.Probe.PoisonFixed = true;
        await Eventually.UntilAsync(() => host.Probe.Count(handler) == 2, "the blocking subscription handles both events",
            timeout: PatientTimeout);

        host.Probe.Events(handler).Should().Equal(new OrderPlaced("poison-1", "alice"), new OrderPlaced("order-2", "bob"));
        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("parked-"));
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task BlockingSubscription_BlocksOnAnUndeserializableEvent_UntilItDeserializes(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Blocking(HandlerKind.Projection);
        {
            await using var first = await TestHost.StartAsync(store, output, o => o
                .Events(Orders.EventTypes)
                .FailDeserializing(typeof(OrderShipped))
                .Handlers(HandlerKind.Projection, handler));
            await store.AppendAsync(first.Serialization, "order-1", new OrderShipped("order-1"));
            await store.AppendAsync(first.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
            await Eventually.QuietAsync();

            first.Probe.Events(handler).Should().BeEmpty("the undeserializable event is never handled");
            (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("parked-"));
            first.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
        }

        await using var second = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler));
        await Eventually.UntilAsync(() => second.Probe.Count(handler) == 2, "the second host handles both events",
            timeout: PatientTimeout);

        second.Probe.Events(handler).Should().Equal(new OrderShipped("order-1"), new OrderPlaced("order-2", "bob"));
        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("parked-"));
    }
}
