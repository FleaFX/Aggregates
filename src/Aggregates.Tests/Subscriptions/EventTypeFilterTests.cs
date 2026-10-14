using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

/// <summary>
/// Scenarios for the server-side event-type filter that an <see cref="EventTypeRegistry"/>
/// in the service collection switches on.
/// </summary>
[Trait("Category", "Integration")]
public class EventTypeFilterTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task FilteredSubscription_DeserializesOnlyItsEventTypes(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(CustomerRegistered)])
            .Handlers(kind, handler));

        await store.AppendAsync(host.Serialization, "customer-1", new CustomerRegistered("alice"));
        await store.AppendAsync(host.Serialization, "customer-2", new CustomerRegistered("bob"));
        await store.AppendAsync(host.Serialization, "customer-3", new CustomerRegistered("carol"));
        await store.AppendRawAsync("other-1", "IntegrationTests.Unknown@v1", "{}"u8.ToArray());
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderShipped("order-2"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 2, "the handler sees both order events");
        await Eventually.QuietAsync();

        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-1", "alice"), new OrderShipped("order-2"));
        host.Probe.DeserializeCalls.Should().NotContainKey(host.Serialization.TypeName(typeof(CustomerRegistered)));
        host.Probe.DeserializeCalls.Should().NotContainKey("IntegrationTests.Unknown@v1");
        if (kind != HandlerKind.Saga) {
            // A saga repository deserializes its own stream again, so only the counts of the other
            // kinds are exactly the number of matching events.
            host.Probe.DeserializeCalls[host.Serialization.TypeName(typeof(OrderPlaced))].Should().Be(1);
            host.Probe.DeserializeCalls[host.Serialization.TypeName(typeof(OrderShipped))].Should().Be(1);
        }
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ObjectHandler_ReceivesEveryEventType(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(CustomerRegistered)])
            .Projections(ScenarioHandlers.ObjectProbe));

        await store.AppendAsync(host.Serialization, "customer-1", new CustomerRegistered("alice"));
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await Eventually.UntilAsync(() => host.Probe.Count(ScenarioHandlers.ObjectProbe) >= 2, "the object handler sees both events");
        await Eventually.QuietAsync();

        host.Probe.Events(ScenarioHandlers.ObjectProbe)
            .Should().Contain(new CustomerRegistered("alice")).And.Contain(new OrderPlaced("order-1", "alice"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task WithoutRegistry_ReceivesEveryEventType(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(CustomerRegistered)])
            .WithoutEventTypeRegistry()
            .Handlers(HandlerKind.Projection, handler));

        await store.AppendAsync(host.Serialization, "customer-1", new CustomerRegistered("alice"));
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "the handler sees the order event");
        await Eventually.QuietAsync();

        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-1", "alice"));
        host.Probe.DeserializeCalls[host.Serialization.TypeName(typeof(CustomerRegistered))].Should().Be(1);
    }

    // MSSP does not report progress yet, so a quiet subscription cannot advance its
    // checkpoint (follow-up in the MSSP backlog).
    [Theory, InlineData(HandlerKind.Projection), InlineData(HandlerKind.Policy), InlineData(HandlerKind.Saga)]
    public async Task QuietSubscription_AdvancesCheckpointThroughProgress_AndSettles(HandlerKind kind) {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(CustomerRegistered)])
            .Handlers(kind, handler)
            .Services(s => s.AddSingleton(new SubscriptionCheckpointOptions { MaxFilteredEvents = 32 })));
        var checkpoint = $"checkpoint-{ProbeCheckpoint(kind)}";

        for (var i = 0; i < 200; i++)
            await store.AppendAsync(host.Serialization, $"customer-{i}", new CustomerRegistered("alice"));

        await Eventually.UntilAsync(
            async () => (await store.ReadAllAsync()).Any(e => e.Stream == checkpoint),
            "the subscription checkpoints progress without handling an event");
        await Eventually.QuietAsync();
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var settled = (await store.ReadAllAsync()).Count(e => e.Stream == checkpoint);
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        host.Probe.Count(handler).Should().Be(0, "the filter excludes every appended event");
        (await store.ReadAllAsync()).Count(e => e.Stream == checkpoint).Should().Be(settled, "the checkpoints settle");
        settled.Should().BeInRange(1, 15, "about one per 32 events read, plus a margin");
    }

    // MSSP cannot tell its end position yet, so a start-from-end subscription that restarts
    // before its first event would skip events appended meanwhile (follow-up in the MSSP backlog).
    [Theory, InlineData(HandlerKind.Projection), InlineData(HandlerKind.Policy), InlineData(HandlerKind.Saga)]
    public async Task StartFromEnd_RestartBeforeFirstEvent_HandlesEventsAppendedMeanwhile(HandlerKind kind) {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        await store.AppendAsync(new SerializationSetup(new HandlerProbe(), Orders.EventTypes), "order-before", new OrderPlaced("order-before", "alice"));
        var handler = ScenarioHandlers.FromEnd(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler))) {
            var checkpoints = first.Services.GetRequiredService<ICheckpointStore>();
            await Eventually.UntilAsync(
                async () => await checkpoints.GetAsync(FromEndCheckpoint(kind), TestContext.Current.CancellationToken) is not null,
                "the end of the store is fixed as the checkpoint of the first start");
        }

        await store.AppendAsync(new SerializationSetup(new HandlerProbe(), Orders.EventTypes), "order-during", new OrderPlaced("order-during", "bob"));
        await using var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        await Eventually.UntilAsync(() => second.Probe.Count(handler) >= 1, "the second host handles the event appended meanwhile");
        await Eventually.QuietAsync();
        second.Probe.Events(handler).Should().Equal(new OrderPlaced("order-during", "bob"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task OldVersionWithUpgrader_PassesTheFilter(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events([.. Orders.EventTypes, typeof(OrderCreated)])
            .Handlers(HandlerKind.Projection, handler));

        await store.AppendAsync(host.Serialization, "order-1", new OrderCreated("order-1"));

        var storedName = host.Serialization.TypeName(typeof(OrderCreated));
        await Eventually.UntilAsync(
            () => host.Probe.DeserializeCalls.TryGetValue(storedName, out var count) && count == 1,
            "the stored version passes the filter");
        await Eventually.QuietAsync();
    }

    static string ProbeCheckpoint(HandlerKind kind) =>
        $"IntegrationTests.{NamespaceOf(kind)}.Probe@v1";

    static string FromEndCheckpoint(HandlerKind kind) =>
        $"IntegrationTests.{NamespaceOf(kind)}.FromEndProbe@v1";

    static string NamespaceOf(HandlerKind kind) => kind switch {
        HandlerKind.Projection => "Projections",
        HandlerKind.Policy => "Policies",
        _ => "Sagas"
    };
}
