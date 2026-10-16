using System.Runtime.CompilerServices;
using System.Text;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas;

/// <summary>
/// Integration tests for saga streams as links: what the handler commits, what a 0.36 stream
/// rehydrates to, unresolved links, and the links-only guard.
/// </summary>
[Trait("Category", "Integration")]
public class SagaStreamLinkTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Handler_CommitsOnlyLinksToTheSourceEvents(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(CountingSaga))));

        var placed = await AppendAsync(store, host, new OrderPlaced("order-1", "alice"));
        var shipped = await AppendAsync(store, host, new OrderShipped("order-1"));
        await HandleAsync(host, new OrderPlaced("order-1", "alice"), placed);
        await HandleAsync(host, new OrderShipped("order-1"), shipped);

        var links = (await store.ReadAllAsync()).Where(e => e.Stream == "saga-1").ToList();
        links.Should().HaveCount(2, "one link per handled source event");
        links.Should().OnlyContain(e => e.EventType == "$>", "a saga stream holds only links");
        links.Select(e => Encoding.UTF8.GetString(e.Data.Span)).Should().Equal("0@order-1", "1@order-1");
        host.Probe.Count<CountingSaga>().Should().Be(2, "each source event triggers one reaction");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task GetAsync_RehydratesAZeroPointThirtySixStream_AndGrowsWithLinks(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(CountingSaga))));

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-1", new OrderShipped("order-1"));
        await AppendZeroPointThirtySixLinkAsync(store, host, "saga-1", "0@order-1");
        await AppendZeroPointThirtySixLinkAsync(store, host, "saga-1", "1@order-1");

        var saga = await GetSagaAsync(host);
        saga.State.Should().Be(new CountingState(2));
        saga.Version.Should().Be(new AggregateVersion(1));

        var third = await AppendAsync(store, host, new OrderPlaced("order-2", "bob"));
        await HandleAsync(host, new OrderPlaced("order-2", "bob"), third);
        host.Probe.Count<CountingSaga>().Should().Be(1, "the hand-written links did not trigger a reaction");

        saga = await GetSagaAsync(host);
        saga.State.Should().Be(new CountingState(3));
        saga.Version.Should().Be(new AggregateVersion(2));
        (await store.ReadAllAsync()).Count(e => e.Stream == "saga-1").Should().Be(3, "the stream grew with one link");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task GetAsync_WithUnresolvedLink_CountsItButAppliesOnlyResolvedEvents(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(CountingSaga))));

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendRawAsync("saga-1", "$>", "5@bestaat-niet"u8.ToArray());
        await AppendZeroPointThirtySixLinkAsync(store, host, "saga-1", "0@order-1");

        var saga = await GetSagaAsync(host);
        saga.State.Should().Be(new CountingState(1), "the unresolved link is not applied");
        saga.Version.Should().Be(new AggregateVersion(1), "the unresolved link counts for the version");

        // The next commit succeeds because the version did not skip the unresolved link.
        var next = await AppendAsync(store, host, new OrderPlaced("order-2", "bob"));
        await HandleAsync(host, new OrderPlaced("order-2", "bob"), next);

        saga = await GetSagaAsync(host);
        saga.State.Should().Be(new CountingState(2));
        saga.Version.Should().Be(new AggregateVersion(2));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task GetAsync_WithPlainEventInSagaStream_Throws(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(CountingSaga))));

        await store.AppendAsync(host.Serialization, "saga-1", new OrderPlaced("order-1", "alice"));

        Func<Task> act = async () => await GetSagaAsync(host);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a saga stream*");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Subscription_WithSagaCorrelatingToAnAggregateId_ParksTheEvent(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        // A misconfigured saga: it correlates the event to the aggregate's own stream.
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(AggregateIdSaga))));

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Any(e => e.Stream.StartsWith("parked-")),
            "the event is parked");
        await Eventually.QuietAsync();
        host.Probe.Count<AggregateIdSaga>().Should().Be(0, "the saga never reacts on an aggregate stream");
        (await store.ReadAllAsync()).Count(e => e.Stream == "order-1").Should().Be(1, "nothing is written to the aggregate stream");
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task HandleAsync_WithRedeliveredSource_LinksAndReactsOnce(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(CountingSaga))));

        var placed = await AppendAsync(store, host, new OrderPlaced("order-1", "alice"));
        // A redelivery after a crash between the saga commit and the checkpoint.
        await HandleAsync(host, new OrderPlaced("order-1", "alice"), placed);
        await HandleAsync(host, new OrderPlaced("order-1", "alice"), placed);

        var saga = await GetSagaAsync(host);
        saga.State.Should().Be(new CountingState(1), "the saga reacts once per source event");
        saga.Version.Should().Be(new AggregateVersion(0));
        (await store.ReadAllAsync()).Count(e => e.Stream == "saga-1").Should().Be(1, "the stream holds one link");
        host.Probe.Count<CountingSaga>().Should().Be(1, "the redelivered source does not trigger a reaction");
    }

    static async Task<EventReference> AppendAsync(IStoreFixture store, TestHost host, IOrderEvent @event) {
        var revision = (ulong)(await store.ReadAllAsync()).Count(e => e.Stream == @event.OrderId);
        await store.AppendAsync(host.Serialization, @event.OrderId, @event);
        return new EventReference(@event.OrderId, revision);
    }

    // Writes a link like 0.36 did: data "n@stream", metadata carrying the Saga key.
    static async Task AppendZeroPointThirtySixLinkAsync(IStoreFixture store, TestHost host, string sagaId, string link) {
        var sagaMetadata = host.Serialization.SerializeMetadata(new EventMetadata(new Dictionary<string, object?> {
            ["Saga"] = new object?[] {
                new Dictionary<string, object?> { ["EventType"] = typeof(OrderPlaced).AssemblyQualifiedName, ["SagaId"] = sagaId }
            }
        }));
        await store.AppendRawAsync(sagaId, "$>", Encoding.UTF8.GetBytes(link), sagaMetadata);
    }

    static async Task<SagaRoot<CountingState, IOrderEvent>> GetSagaAsync(TestHost host) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISagaRepository<CountingState, IOrderEvent>>()
            .GetAsync("saga-1", TestContext.Current.CancellationToken);
    }

    static async Task HandleAsync(TestHost host, IOrderEvent @event, EventReference source) {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LoggingSagaHandler<CountingSaga, CountingState, IOrderEvent>>()
            .HandleAsync("saga-1", @event, source, TestContext.Current.CancellationToken);
    }

    sealed record CountingState(int Seen) : IState<CountingState, IOrderEvent> {
        public static CountingState Initial => new(0);
        public CountingState Apply(IOrderEvent @event) => this with { Seen = Seen + 1 };
    }

    [SagaContract("LinkCounting", @namespace: "IntegrationTests")]
    sealed class CountingSaga(HandlerProbe probe) : ISaga<CountingState, IOrderEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IOrderEvent @event, EventMetadata metadata) => [];

        public async IAsyncEnumerable<ICommand> ReactAsync(CountingState state, IOrderEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }

    [SagaContract("AggregateIdCounting", @namespace: "IntegrationTests")]
    sealed class AggregateIdSaga(HandlerProbe probe) : ISaga<CountingState, IOrderEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IOrderEvent @event, EventMetadata metadata) =>
            [new AggregateIdentifier(@event.OrderId)];

        public async IAsyncEnumerable<ICommand> ReactAsync(CountingState state, IOrderEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }
}
