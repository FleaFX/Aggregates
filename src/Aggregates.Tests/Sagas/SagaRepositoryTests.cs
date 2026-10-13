using Aggregates.Subscriptions;
using Aggregates.Testing;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas;

[Trait("Category", "Integration")]
public class SagaRepositoryTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task GetAsync_RehydratesStateCommittedByHandler(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        // No resolver, so no subscription: the saga is only driven through its handler here.
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Sagas(s => s.ScanTypes(typeof(CountingSaga))));

        var placed = await AppendAsync(store, host, new OrderPlaced("order-1", "alice"));
        var shipped = await AppendAsync(store, host, new OrderShipped("order-1"));
        await HandleAsync(host, new OrderPlaced("order-1", "alice"), placed);
        await HandleAsync(host, new OrderShipped("order-1"), shipped);

        await using var scope = host.Services.CreateAsyncScope();
        var saga = await scope.ServiceProvider.GetRequiredService<ISagaRepository<CountingState, IOrderEvent>>()
            .GetAsync("saga-1", TestContext.Current.CancellationToken);
        saga.State.Should().Be(new CountingState(2));
        saga.Version.Should().Be(new AggregateVersion(1));

        var links = (await store.ReadAllAsync()).Where(e => e.Stream == "saga-1").ToList();
        links.Should().HaveCount(2);
        links.Should().OnlyContain(e => e.EventType == "$>", "a saga stream holds only links");
        links.Select(e => Encoding.UTF8.GetString(e.Data.Span)).Should().Equal("0@order-1", "1@order-1");
    }

    static async Task<EventReference> AppendAsync(IStoreFixture store, TestHost host, IOrderEvent @event) {
        var revision = (ulong)(await store.ReadAllAsync()).Count(e => e.Stream == @event.OrderId);
        await store.AppendAsync(host.Serialization, @event.OrderId, @event);
        return new EventReference(@event.OrderId, revision);
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

    [SagaContract("Counting")]
    sealed class CountingSaga : ISaga<CountingState, IOrderEvent> {
        public IAsyncEnumerable<ICommand> ReactAsync(CountingState state, IOrderEvent @event, CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ICommand>();
    }
}
