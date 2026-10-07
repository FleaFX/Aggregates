using Aggregates.Testing;
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

        await HandleAsync(host, new OrderPlaced("order-1", "alice"));
        await HandleAsync(host, new OrderShipped("order-1"));

        await using var scope = host.Services.CreateAsyncScope();
        var saga = await scope.ServiceProvider.GetRequiredService<ISagaRepository<CountingState, IOrderEvent>>()
            .GetAsync("saga-1", TestContext.Current.CancellationToken);
        saga.State.Should().Be(new CountingState(2));
        saga.Version.Should().Be(new AggregateVersion(1));
    }

    static async Task HandleAsync(TestHost host, IOrderEvent @event) {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISagaHandler<CountingState, IOrderEvent>>()
            .HandleAsync("saga-1", @event, TestContext.Current.CancellationToken);
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
