using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

/// <summary>
/// End-to-end scenarios for a contract version bump: the new subscription continues from the
/// checkpoint of the nearest predecessor that has one, on both transports and for every
/// handler kind.
/// </summary>
[Trait("Category", "Integration")]
public class ContinueFromTests(ITestOutputHelper output) {
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task VersionBump_ContinuesAfterTheOldVersionsCheckpoint(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var firstHandler = ScenarioHandlers.Probe(kind);
        var secondHandler = ScenarioHandlers.ProbeV2(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, firstHandler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(firstHandler) >= 1, "the first host handles order-1");
            // Let the handling of order-1 complete; its checkpoint is written when the host stops.
            await Eventually.QuietAsync();
        }

        await using (var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, secondHandler))) {
            // The adoption happens before the first subscription, but the new checkpoint is only
            // observable through the store.
            await Eventually.UntilAsync(async () =>
                await second.Services.GetRequiredService<ICheckpointStore>().GetAsync(Id(kind, 2), Token) is not null,
                "v2 adopts the v1 checkpoint");
            await store.AppendAsync(second.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

            await Eventually.UntilAsync(() => second.Probe.Count(secondHandler) >= 1, "the second host handles order-2");
            await Eventually.QuietAsync();
            second.Probe.Events(secondHandler).Should().Equal(new OrderPlaced("order-2", "bob"));
            (await second.Services.GetRequiredService<ICheckpointStore>().GetAsync(Id(kind, 1), Token))
                .Should().NotBeNull("the checkpoint of the old version is kept");
        }
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task WithoutAPredecessorCheckpoint_StartsFromTheBeginning(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.ProbeV2(kind);
        await store.AppendAsync(new SerializationSetup(new HandlerProbe(), Orders.EventTypes), "order-1", new OrderPlaced("order-1", "alice"));

        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "v2 replays order-1 from the beginning");
        await Eventually.QuietAsync();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-1", "alice"));
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task WithoutAPredecessorCheckpoint_AndStartFromEnd_StartsFromTheEnd(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.FromEndBump(kind);
        await store.AppendAsync(new SerializationSetup(new HandlerProbe(), Orders.EventTypes), "order-1", new OrderPlaced("order-1", "alice"));

        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));

        // There is no signal for "subscription established", so keep appending until one is handled.
        // Everything appended before the start is skipped, like order-1.
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
    public async Task WithContinueFromNone_ReplaysFromTheBeginning(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var firstHandler = ScenarioHandlers.Probe(kind);
        var secondHandler = ScenarioHandlers.ReplayProbeV2(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, firstHandler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(firstHandler) >= 1, "the first host handles order-1");
            await Eventually.QuietAsync();
        }

        await using var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, secondHandler));

        await Eventually.UntilAsync(() => second.Probe.Count(secondHandler) >= 1, "v2 replays order-1 despite the v1 checkpoint");
        await Eventually.QuietAsync();
        second.Probe.Events(secondHandler).Should().Equal(new OrderPlaced("order-1", "alice"));
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task AfterTheBump_ItsOwnCheckpointWinsOverThePredecessors(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var firstHandler = ScenarioHandlers.Probe(kind);
        var secondHandler = ScenarioHandlers.ProbeV2(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, firstHandler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(firstHandler) >= 1, "the first host handles order-1");
            await Eventually.QuietAsync();
        }

        await using (var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, secondHandler))) {
            await Eventually.UntilAsync(async () =>
                await second.Services.GetRequiredService<ICheckpointStore>().GetAsync(Id(kind, 2), Token) is not null,
                "v2 adopts the v1 checkpoint");

            // Move the v1 checkpoint far ahead of the store: a restart of v2 must not adopt it again.
            await second.Services.GetRequiredService<ICheckpointStore>().StoreAsync(Id(kind, 1), ulong.MaxValue - 1, Token);
        }

        await using var third = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, secondHandler));
        await store.AppendAsync(third.Serialization, "order-3", new OrderPlaced("order-3", "carol"));

        await Eventually.UntilAsync(() => third.Probe.Count(secondHandler) >= 1, "the third host handles order-3, not the end of the store");
        await Eventually.QuietAsync();
        third.Probe.Events(secondHandler).Should().Equal(new OrderPlaced("order-3", "carol"));
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task ASkippedVersion_ContinuesFromTheOlderPredecessor(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var firstHandler = ScenarioHandlers.Probe(kind);
        var thirdHandler = ScenarioHandlers.ProbeV3(kind);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, firstHandler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(firstHandler) >= 1, "the first host handles order-1");
            await Eventually.QuietAsync();
        }

        await using var third = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, thirdHandler));
        await store.AppendAsync(third.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(() => third.Probe.Count(thirdHandler) >= 1, "v3 handles order-2 and does not replay order-1");
        await Eventually.QuietAsync();
        third.Probe.Events(thirdHandler).Should().Equal(new OrderPlaced("order-2", "bob"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SagaVersionBump_KeepsTheSagaState(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var firstHandler = ScenarioHandlers.Probe(HandlerKind.Saga);
        var secondHandler = ScenarioHandlers.ProbeV2(HandlerKind.Saga);

        await using (var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Saga, firstHandler))) {
            await store.AppendAsync(first.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
            await Eventually.UntilAsync(() => first.Probe.Count(firstHandler) >= 1, "the v1 saga handles order-1");
            await Eventually.QuietAsync();
        }

        await using var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Saga, secondHandler));
        await store.AppendAsync(second.Serialization, "order-1", new OrderShipped("order-1"));

        await Eventually.UntilAsync(() => second.Probe.Count(secondHandler) >= 1, "the v2 saga handles the shipped event");
        await Eventually.QuietAsync();
        second.Probe.Events(secondHandler).Should().Equal(new OrderShipped("order-1"));

        // The saga stream is the saga id, so v2 continued in the stream of v1.
        var sagaStream = (await store.ReadAllAsync()).Where(e => e.Stream == "saga-order-1").ToArray();
        sagaStream.Should().HaveCount(2, "v1 and v2 each linked one event into the same saga stream");
        sagaStream.Should().OnlyContain(e => e.EventType == "$>");
    }

    // The subscription id of the Probe contract of <paramref name="kind"/> at <paramref name="version"/>.
    static string Id(HandlerKind kind, int version) =>
        $"IntegrationTests.{NamespaceOf(kind)}.Probe@v{version}";

    static string NamespaceOf(HandlerKind kind) => kind switch {
        HandlerKind.Projection => "Projections",
        HandlerKind.Policy => "Policies",
        _ => "Sagas"
    };
}
