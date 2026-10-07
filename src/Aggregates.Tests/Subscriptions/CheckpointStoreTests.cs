using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

[Trait("Category", "Integration")]
public class CheckpointStoreTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task GetAsync_ReturnsNull_ForUnknownSubscription(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Projections());
        var checkpoints = host.Services.GetRequiredService<ICheckpointStore>();

        (await checkpoints.GetAsync("unknown", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task GetAsync_ReturnsLastStoredPosition(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Projections());
        var checkpoints = host.Services.GetRequiredService<ICheckpointStore>();

        await checkpoints.StoreAsync("subscription-1", 10, TestContext.Current.CancellationToken);
        await checkpoints.StoreAsync("subscription-1", 20, TestContext.Current.CancellationToken);

        (await checkpoints.GetAsync("subscription-1", TestContext.Current.CancellationToken)).Should().Be(20);
    }
}
