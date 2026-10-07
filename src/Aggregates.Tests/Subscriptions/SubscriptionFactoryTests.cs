using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

[Trait("Category", "Integration")]
public class SubscriptionFactoryTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_FromStart_DeliversExistingEventsWithMetadata(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"), ("customer", "alice"));
        await store.AppendAsync(host.Serialization, "order-1", new OrderShipped("order-1"));

        var messages = await host.Services.GetRequiredService<ISubscriptionFactory>().TakeAsync(null, false, 2);

        messages.Select(m => m.Event).Should().Equal(new OrderPlaced("order-1", "alice"), new OrderShipped("order-1"));
        messages[0].Metadata.Should().Contain("customer", "alice");
        messages[1].Metadata.Should().BeEmpty();
        messages[1].CommitPosition.Should().BeGreaterThan(messages[0].CommitPosition);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_FromPosition_DeliversOnlyLaterEvents(Transport transport) {
        Assert.SkipWhen(transport == Transport.MSSP, KnownIssues.MsspStartPositionInclusive);
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-1", new OrderShipped("order-1"));
        var factory = host.Services.GetRequiredService<ISubscriptionFactory>();
        var first = (await factory.TakeAsync(null, false, 1)).Single();

        var messages = await factory.TakeAsync(first.CommitPosition, false, 1);

        messages.Single().Event.Should().Be(new OrderShipped("order-1"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_FromEnd_DeliversOnlyNewEvents(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        await store.AppendAsync(host.Serialization, "order-old", new OrderPlaced("order-old", "alice"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Eventually.DefaultTimeout);
        await using var subscription = host.Services.GetRequiredService<ISubscriptionFactory>().Subscribe(null, true, timeout.Token);
        var first = FirstAsync(subscription, timeout.Token);

        // There is no signal for "subscription established", so keep appending until one arrives.
        for (var i = 0; !first.IsCompleted; i++) {
            await store.AppendAsync(host.Serialization, $"order-new-{i}", new OrderPlaced($"order-new-{i}", "bob"));
            await Task.WhenAny(first, Task.Delay(200, timeout.Token));
        }

        (await first).Event.Should().BeOfType<OrderPlaced>().Which.OrderId.Should().StartWith("order-new-");
    }

    static async Task<SubscriptionMessage> FirstAsync(ISubscription subscription, CancellationToken cancellationToken) {
        await foreach (var message in subscription.WithCancellation(cancellationToken))
            return message;
        throw new InvalidOperationException("The subscription ended without messages.");
    }
}
