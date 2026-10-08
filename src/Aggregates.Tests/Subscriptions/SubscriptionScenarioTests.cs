using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

/// <summary>
/// End-to-end scenarios that behave the same for projections, policies and sagas.
/// </summary>
[Trait("Category", "Integration")]
public class SubscriptionScenarioTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task Restart_ContinuesAfterCheckpoint(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(handler) >= 1, "the first host handles order-1");
            // Let the handling of order-1 complete; its checkpoint is written when the host stops.
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

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task SingleEvent_IsHandledOnce_AndCheckpointsSettle(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Handlers(kind, handler)
            .Services(CheckpointEveryMessage));

        await AssertHandledOnceAndSettledAsync(store, host, [handler]);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SingleEvent_IsHandledOnceByEachKind_AndCheckpointsSettle(Transport transport) {
        // The saga joins once its event copies no longer reach the other subscriptions.
        HandlerKind[] kinds = [HandlerKind.Projection, HandlerKind.Policy];
        await using var store = await Stores.StartAsync(transport);
        var handlers = kinds.Select(ScenarioHandlers.Probe).ToArray();
        await using var host = await TestHost.StartAsync(store, output, o => {
            o.Events(Orders.EventTypes).Services(CheckpointEveryMessage);
            foreach (var kind in kinds)
                o.Handlers(kind, ScenarioHandlers.Probe(kind));
        });

        await AssertHandledOnceAndSettledAsync(store, host, handlers);
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
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

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task UnknownEventType_IsSkipped_NotParked(HandlerKind kind, Transport transport) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        // The serializer doesn't know this type, so Deserialize returns null.
        await store.AppendRawAsync("other-1", "IntegrationTests.Unknown@v1", "{}"u8.ToArray());
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1 || host.Stopping.IsCompleted, "order-2 is handled after the unknown event");
        await Eventually.QuietAsync();
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-2", "bob"));
        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("parked-"));
    }

    // E9 covers projections; the store can only be interrupted on KurrentDB.
    [Theory, InlineData(HandlerKind.Policy), InlineData(HandlerKind.Saga)]
    public async Task StoreInterruption_SubscribesAgain(HandlerKind kind) {
        Assert.SkipWhen(kind == HandlerKind.Saga, KnownIssues.SagaEventCopies);
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "order-1 is handled");

        await store.InterruptAsync();
        await Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await store.ResumeAsync();
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 2 || host.Stopping.IsCompleted,
            "order-2 is handled after the interruption", TimeSpan.FromSeconds(60));
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-1", "alice"), new OrderPlaced("order-2", "bob"));
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
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

    // Without batching every message writes a checkpoint, so a checkpoint event that is delivered
    // again shows up as a growing checkpoint stream.
    static void CheckpointEveryMessage(IServiceCollection services) =>
        services.AddSingleton(new SubscriptionCheckpointOptions { MaxBatchSize = 1 });

    static async Task AssertHandledOnceAndSettledAsync(IStoreFixture store, TestHost host, Type[] handlers) {
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await Eventually.UntilAsync(() => handlers.All(h => host.Probe.Count(h) >= 1), "every handler handles order-1");
        await Eventually.UntilAsync(async () => (await CountCheckpointsAsync(store)).Count == handlers.Length,
            "every subscription checkpoints order-1");
        var checkpointsBefore = await CountCheckpointsAsync(store);
        await Eventually.QuietAsync();
        var checkpointsAfter = await CountCheckpointsAsync(store);

        foreach (var handler in handlers)
            host.Probe.Count(handler).Should().Be(1, $"{handler.Name} handles order-1 once");
        checkpointsAfter.Should().Equal(checkpointsBefore, "an idle store does not get new checkpoints");
        checkpointsAfter.Values.Should().OnlyContain(count => count == 1, "each subscription checkpoints order-1 and nothing else");
        host.Probe.DeserializeCalls.Keys.Should().BeSubsetOf(Orders.EventTypes.Select(host.Serialization.TypeName),
            "only domain events are deserialized");
    }

    static async Task<Dictionary<string, int>> CountCheckpointsAsync(IStoreFixture store) =>
        (await store.ReadAllAsync())
            .Where(e => e.Stream.StartsWith("checkpoint-"))
            .CountBy(e => e.Stream)
            .ToDictionary();
}
