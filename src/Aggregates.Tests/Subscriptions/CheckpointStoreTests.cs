using Aggregates.Testing;
using FluentAssertions;
using KurrentDB.Client;
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

    [Fact]
    public async Task StoreAsync_RetainsOnlyLatestCheckpoint_OnKurrentDB() {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        await using var host = await TestHost.StartAsync(store, output, o => o.Projections());
        var checkpoints = host.Services.GetRequiredService<ICheckpointStore>();
        var client = host.Services.GetRequiredService<KurrentDBClient>();

        await checkpoints.StoreAsync("subscription-1", 10, TestContext.Current.CancellationToken);
        await checkpoints.StoreAsync("subscription-1", 20, TestContext.Current.CancellationToken);

        var metadata = await client.GetStreamMetadataAsync("checkpoint-subscription-1", cancellationToken: TestContext.Current.CancellationToken);
        metadata.Metadata.MaxCount.Should().Be(1);
        var events = await client.ReadStreamAsync(Direction.Forwards, "checkpoint-subscription-1", StreamPosition.Start,
            cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        events.Should().ContainSingle();
    }
}
