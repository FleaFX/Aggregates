using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Subscriptions;

/// <summary>
/// End-to-end scenarios that behave the same for projections, policies and sagas.
/// </summary>
[Trait("Category", "Integration")]
public class SubscriptionScenarioTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task Restart_ContinuesAfterCheckpoint(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        // Passes on MSSP today only because checkpoint events move the checkpoint past order-1.
        Assert.SkipWhen(transport == Transport.MSSP, KnownIssues.MsspStartPositionInclusive);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(handler) >= 1, "the first host handles order-1");
            // Give the subscription time to store the checkpoint after handling.
            await Eventually.QuietAsync();
        }

        await using var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));
        await store.AppendAsync(second.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => second.Probe.Count(handler) >= 1, "the second host handles order-2");
        await Eventually.QuietAsync();
        second.Probe.Events(handler).Should().Equal(new OrderPlaced("order-2", "bob"));
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task StartFromEnd_SkipsEventsBeforeFirstStart(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.FromEnd(kind);
        await store.AppendAsync(new SerializationSetup(new HandlerProbe(), Orders.EventTypes), "order-before", new OrderPlaced("order-before", "alice"));
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        // There is no signal for "subscription established", so keep appending until one is handled.
        for (var i = 0; host.Probe.Count(handler) == 0; i++) {
            await store.AppendAsync(host.Serialization, $"order-after-{i}", new OrderPlaced($"order-after-{i}", "bob"));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            i.Should().BeLessThan(75, "an event appended after the start is eventually handled");
        }

        await Eventually.QuietAsync();
        host.Probe.Events(handler).Cast<IOrderEvent>().Should()
            .OnlyContain(e => e.OrderId.StartsWith("order-after-"))
            .And.OnlyHaveUniqueItems();
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task FailingHandler_ParksEventAndContinues(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        await store.AppendAsync(host.Serialization, $"{ScenarioHandlers.PoisonPrefix}-1", new OrderPlaced($"{ScenarioHandlers.PoisonPrefix}-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "order-2 is handled after the poison event");
        await Eventually.QuietAsync();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-2", "bob"));
        (await store.ReadAllAsync()).Should().ContainSingle(e => e.Stream.StartsWith("parked-"));
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
    }

    [Theory(Skip = KnownIssues.CheckpointFeedback), MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task SingleEvent_IsHandledOnce_AndCheckpointsSettle(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        await AssertHandledOnceAndSettledAsync(store, host, [handler]);
    }

    [Theory(Skip = KnownIssues.CheckpointFeedback), MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SingleEvent_IsHandledOnceByEachKind_AndCheckpointsSettle(Transport transport) {
        Assert.Skip(KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handlers = Enum.GetValues<HandlerKind>().Select(ScenarioHandlers.Probe).ToArray();
        await using var host = await TestHost.StartAsync(store, output, o => {
            o.Events(Orders.EventTypes);
            foreach (var kind in Enum.GetValues<HandlerKind>())
                o.Handlers(kind, ScenarioHandlers.Probe(kind));
        });

        await AssertHandledOnceAndSettledAsync(store, host, handlers);
    }

    [Theory(Skip = KnownIssues.HostStopsOnSubscriptionError), MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task FailingDeserialization_ParksEventAndContinues(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .FailDeserializing(typeof(OrderShipped))
            .Handlers(kind, handler));

        await store.AppendAsync(host.Serialization, "order-1", new OrderShipped("order-1"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1 || host.Stopping.IsCompleted, "order-2 is handled after the poison event");
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
        await Eventually.QuietAsync();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-2", "bob"));
        (await store.ReadAllAsync()).Should().ContainSingle(e => e.Stream.StartsWith("parked-"));
    }

    [Theory(Skip = KnownIssues.OneHandlerPerEventType), MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task TwoHandlersOfSameKindOnSameEvent_BothHandleIt(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var first = ScenarioHandlers.Probe(kind);
        var second = ScenarioHandlers.SecondProbe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, first, second));

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await Eventually.UntilAsync(() => host.Probe.Count(first) >= 1 && host.Probe.Count(second) >= 1, "both handlers handle order-1");
        await Eventually.QuietAsync();
        host.Probe.Count(first).Should().Be(1);
        host.Probe.Count(second).Should().Be(1);
    }

    static async Task AssertHandledOnceAndSettledAsync(IStoreFixture store, TestHost host, Type[] handlers) {
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await Eventually.UntilAsync(() => handlers.All(h => host.Probe.Count(h) >= 1), "every handler handles order-1");
        var checkpointsBefore = await CountCheckpointsAsync(store);
        await Eventually.QuietAsync();
        var checkpointsAfter = await CountCheckpointsAsync(store);

        foreach (var handler in handlers)
            host.Probe.Count(handler).Should().Be(1, $"{handler.Name} handles order-1 once");
        checkpointsAfter.Should().Be(checkpointsBefore, "an idle store does not get new checkpoints");
        host.Probe.DeserializeCalls.Keys.Should().BeSubsetOf(Orders.EventTypes.Select(host.Serialization.TypeName),
            "only domain events are deserialized");
    }

    static async Task<int> CountCheckpointsAsync(IStoreFixture store) =>
        (await store.ReadAllAsync()).Count(e => e.Stream.StartsWith("checkpoint-"));
}
