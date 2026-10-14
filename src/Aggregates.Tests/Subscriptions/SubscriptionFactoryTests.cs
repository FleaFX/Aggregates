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
    public async Task Subscribe_ReportsFailingDeserialize_AndContinues(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .FailDeserializing(typeof(OrderShipped))
            .Projections());
        await store.AppendAsync(host.Serialization, "order-1", new OrderShipped("order-1"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        var messages = await host.Services.GetRequiredService<ISubscriptionFactory>().TakeAsync(null, false, 2);

        messages[0].Event.Should().BeNull();
        messages[0].DeserializationError.Should().BeOfType<InvalidOperationException>();
        messages[1].Event.Should().Be(new OrderPlaced("order-2", "bob"));
        messages[1].DeserializationError.Should().BeNull();
        messages[1].CommitPosition.Should().BeGreaterThan(messages[0].CommitPosition);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_ReportsFailingDeserializeMetadata_AndContinues(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        await store.AppendRawAsync("order-1", host.Serialization.TypeName(typeof(OrderPlaced)),
            host.Serialization.SerializeData(new OrderPlaced("order-1", "alice")), "not json"u8.ToArray());
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        var messages = await host.Services.GetRequiredService<ISubscriptionFactory>().TakeAsync(null, false, 2);

        messages[0].Event.Should().BeNull();
        messages[0].Metadata.Should().BeSameAs(EventMetadata.Empty);
        messages[0].DeserializationError.Should().NotBeNull();
        messages[1].Event.Should().Be(new OrderPlaced("order-2", "bob"));
        messages[1].DeserializationError.Should().BeNull();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_SkipsEventsWithSystemEventType(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        await store.AppendRawAsync("checkpoint-subscription-1", "$aggregates-checkpoint", new byte[sizeof(ulong)]);
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        var messages = await host.Services.GetRequiredService<ISubscriptionFactory>().TakeAsync(null, false, 1);

        messages.Single().Event.Should().Be(new OrderPlaced("order-1", "alice"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_FromEnd_DeliversOnlyNewEvents(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        await store.AppendAsync(host.Serialization, "order-old", new OrderPlaced("order-old", "alice"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Eventually.DefaultTimeout);
        await using var subscription = host.Services.GetRequiredService<ISubscriptionFactory>().Subscribe(new SubscriptionRequest(null, true), timeout.Token);
        var first = FirstAsync(subscription, timeout.Token);

        // There is no signal for "subscription established", so keep appending until one arrives.
        for (var i = 0; !first.IsCompleted; i++) {
            await store.AppendAsync(host.Serialization, $"order-new-{i}", new OrderPlaced($"order-new-{i}", "bob"));
            await Task.WhenAny(first, Task.Delay(200, timeout.Token));
        }

        (await first).Event.Should().BeOfType<OrderPlaced>().Which.OrderId.Should().StartWith("order-new-");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_WithEventTypes_DeliversOnlyThoseTypes(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events([.. Orders.EventTypes, typeof(CustomerRegistered)]).Projections());
        var serialization = host.Serialization;
        await store.AppendAsync(serialization, "customer-1", new CustomerRegistered("alice"));
        await store.AppendAsync(serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendRawAsync("other-1", "IntegrationTests.Unknown@v1", "{}"u8.ToArray());
        await store.AppendAsync(serialization, "order-2", new OrderShipped("order-2"));
        var factory = host.Services.GetRequiredService<ISubscriptionFactory>();

        var request = new SubscriptionRequest(null, false,
            [serialization.TypeName(typeof(OrderPlaced)), serialization.TypeName(typeof(OrderShipped))]);
        var messages = await factory.TakeAsync(request, 2);

        messages.Select(m => m.Event).Should().Equal(new OrderPlaced("order-1", "alice"), new OrderShipped("order-2"));
        host.Probe.DeserializeCalls.Should().NotContainKeys(
            serialization.TypeName(typeof(CustomerRegistered)), "IntegrationTests.Unknown@v1");
    }

    [Fact]
    public async Task Subscribe_WithEventTypes_ReportsProgress() {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events([.. Orders.EventTypes, typeof(CustomerRegistered)]).Projections());
        var serialization = host.Serialization;
        for (var i = 0; i < 100; i++)
            await store.AppendAsync(serialization, $"customer-{i}", new CustomerRegistered("alice"));
        await store.AppendAsync(serialization, "order-1", new OrderPlaced("order-1", "alice"));
        var factory = host.Services.GetRequiredService<ISubscriptionFactory>();

        var request = new SubscriptionRequest(null, false, [serialization.TypeName(typeof(OrderPlaced))], ProgressInterval: 32);
        var messages = await factory.TakeUntilAsync(request, m => m.Event is OrderPlaced);

        var progress = messages.Where(m => m.IsProgress).ToList();
        progress.Should().NotBeEmpty();
        progress.All(m => m.Event is null).Should().BeTrue();
        var orderPlaced = messages.Single(m => m.Event is OrderPlaced);
        progress.Should().OnlyContain(m => m.CommitPosition < orderPlaced.CommitPosition);
        for (var i = 1; i < progress.Count; i++)
            progress[i].CommitPosition.Should().BeGreaterThan(progress[i - 1].CommitPosition);
    }

    [Fact]
    public async Task GetEndPosition_ReturnsPositionOfLastEvent() {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
        var factory = host.Services.GetRequiredService<ISubscriptionFactory>();
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        var end = await factory.GetEndPositionAsync(TestContext.Current.CancellationToken);

        end.Should().NotBeNull();
        await store.AppendAsync(host.Serialization, "order-2", new OrderShipped("order-2"));
        var messages = await factory.TakeAsync(new SubscriptionRequest(end, false), 1);
        messages.Single().Event.Should().Be(new OrderShipped("order-2"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscribe_WithoutEventTypes_DeliversEveryType(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events([.. Orders.EventTypes, typeof(CustomerRegistered)]).Projections());
        var serialization = host.Serialization;
        await store.AppendAsync(serialization, "customer-1", new CustomerRegistered("alice"));
        await store.AppendAsync(serialization, "order-1", new OrderPlaced("order-1", "alice"));
        var factory = host.Services.GetRequiredService<ISubscriptionFactory>();

        var messages = await factory.TakeAsync(new SubscriptionRequest(null, false), 2);

        messages.Select(m => m.Event).Should().Equal(new CustomerRegistered("alice"), new OrderPlaced("order-1", "alice"));
    }

    static async Task<SubscriptionMessage> FirstAsync(ISubscription subscription, CancellationToken cancellationToken) {
        await foreach (var message in subscription.WithCancellation(cancellationToken))
            return message;
        throw new InvalidOperationException("The subscription ended without messages.");
    }
}
