using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

/// <summary>
/// Integration tests for listing, replaying and dismissing parked messages through
/// <see cref="IParkedMessages"/>, on both transports.
/// </summary>
[Trait("Category", "Integration")]
public class ParkedMessageReplayTests(ITestOutputHelper output) {

    static string SubscriptionIdOf(TestHost host) =>
        host.Services.GetServices<SubscriptionRegistration>().Single().SubscriptionId;

    static IParkedMessages ParkedMessagesOf(TestHost host) =>
        host.Services.GetRequiredService<IParkedMessages>();

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task ParkedEvent_IsListed_WithItsSourceAndException(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));
        var parked = ParkedMessagesOf(host);
        var id = SubscriptionIdOf(host);

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
            "the poison event is parked");

        var message = (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Should().ContainSingle().Which;
        message.Source.Should().Be(new EventReference("poison-1", 0));
        message.EventType.Should().Be(typeof(OrderPlaced).FullName);
        message.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        message.ExceptionMessage.Should().Contain("on purpose");
        message.Revision.Should().Be(0);
        message.ParkedAt.Should().NotBe(default);
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task ParkedEvent_IsReplayed_AfterTheCauseIsFixed(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));
        var parked = ParkedMessagesOf(host);
        var id = SubscriptionIdOf(host);

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
            "the poison event is parked");
        await Eventually.UntilAsync(() => host.Probe.Count(handler) == 1, "the subscription handles order-2");
        host.Probe.PoisonFixed = true;

        var replay = await parked.ReplayAsync(id, 0, TestContext.Current.CancellationToken);

        replay.Succeeded.Should().BeTrue();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-2", "bob"), new OrderPlaced("poison-1", "alice"));
        (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await store.ReadAllAsync()).Should()
            .ContainSingle(e => e.Stream == $"parked-{id}" && e.EventType == ParkedFormat.ReplayedEventType);
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task Replay_WhileTheCauseRemains_Fails_AndKeepsTheMessage(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));
        var parked = ParkedMessagesOf(host);
        var id = SubscriptionIdOf(host);

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
            "the poison event is parked");

        var replay = await parked.ReplayAsync(id, 0, TestContext.Current.CancellationToken);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("on purpose");
        (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Should().ContainSingle();
        (await store.ReadAllAsync()).Should().NotContain(e => e.EventType == ParkedFormat.ReplayedEventType);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Dismiss_RemovesTheMessage_WithoutHandlingIt(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler));
        var parked = ParkedMessagesOf(host);
        var id = SubscriptionIdOf(host);

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
            "the poison event is parked");
        await Eventually.UntilAsync(() => host.Probe.Count(handler) == 1, "the subscription handles order-2");

        await parked.DismissAsync(id, 0, TestContext.Current.CancellationToken);

        (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Should().BeEmpty();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-2", "bob"));
        (await store.ReadAllAsync()).Should()
            .ContainSingle(e => e.Stream == $"parked-{id}" && e.EventType == ParkedFormat.DismissedEventType);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ReplayAll_ReplaysEveryListedMessage_InOrder(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler));
        var parked = ParkedMessagesOf(host);
        var id = SubscriptionIdOf(host);

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await store.AppendAsync(host.Serialization, "poison-2", new OrderPlaced("poison-2", "bob"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 2,
            "both poison events are parked");
        host.Probe.PoisonFixed = true;

        var replays = await parked.ReplayAllAsync(id, TestContext.Current.CancellationToken);

        replays.Should().HaveCount(2);
        replays.Should().OnlyContain(replay => replay.Succeeded);
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("poison-1", "alice"), new OrderPlaced("poison-2", "bob"));
        (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task ParkedEventsOfAPredecessor_AreReplayedByItsSuccessor(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var v1 = ScenarioHandlers.Probe(kind);
        var v2 = ScenarioHandlers.ProbeV2(kind);
        string id;
        {
            await using var first = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, v1));
            var parked = ParkedMessagesOf(first);
            id = SubscriptionIdOf(first);
            await store.AppendAsync(first.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
            await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
                "the poison event is parked");
            await Eventually.QuietAsync();
        }

        await using var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, v2));
        second.Probe.PoisonFixed = true;
        var parkedOfSecond = ParkedMessagesOf(second);

        var replay = await parkedOfSecond.ReplayAsync(id, 0, TestContext.Current.CancellationToken);

        replay.Succeeded.Should().BeTrue();
        second.Probe.Events(v2).Should().Equal(new OrderPlaced("poison-1", "alice"));
        (await parkedOfSecond.ListAsync(id, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task UndeserializableEvent_IsReplayed_OnceItDeserializes(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        {
            await using var first = await TestHost.StartAsync(store, output, o => o
                .Events(Orders.EventTypes)
                .FailDeserializing(typeof(OrderShipped))
                .Handlers(HandlerKind.Projection, handler));
            var parked = ParkedMessagesOf(first);
            var id = SubscriptionIdOf(first);
            await store.AppendAsync(first.Serialization, "order-1", new OrderShipped("order-1"));
            await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
                "the undeserializable event is parked");
            (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Should().ContainSingle().Which.EventType.Should().BeNull();
            await Eventually.QuietAsync();
        }

        await using var second = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler));
        var parkedOfSecond = ParkedMessagesOf(second);
        var idOfSecond = SubscriptionIdOf(second);

        var replay = await parkedOfSecond.ReplayAsync(idOfSecond, 0, TestContext.Current.CancellationToken);

        replay.Succeeded.Should().BeTrue();
        second.Probe.Events(handler).Should().Equal(new OrderShipped("order-1"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Replay_WithoutTheHandlerInThisHost_Fails(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        string id;
        {
            await using var first = await TestHost.StartAsync(store, output,
                o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler));
            var parked = ParkedMessagesOf(first);
            id = SubscriptionIdOf(first);
            await store.AppendAsync(first.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
            await Eventually.UntilAsync(async ValueTask<bool> () => (await parked.ListAsync(id, TestContext.Current.CancellationToken)).Count == 1,
                "the poison event is parked");
            await Eventually.QuietAsync();
        }

        await using var second = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        var parkedOfSecond = ParkedMessagesOf(second);

        (await parkedOfSecond.ListAsync(id, TestContext.Current.CancellationToken)).Should().ContainSingle();

        var replay = await parkedOfSecond.ReplayAsync(id, 0, TestContext.Current.CancellationToken);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("No subscription in this host");
        (await parkedOfSecond.ListAsync(id, TestContext.Current.CancellationToken)).Should().ContainSingle();
    }
}
