using System.Text.Json;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

[Trait("Category", "Integration")]
public class ParkedMessageSinkTests(ITestOutputHelper output) {
    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ParkAsync_WritesOneEventWithPositionAndException(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Projections());
        var sink = host.Services.GetRequiredService<IParkedMessageSink>();

        await sink.ParkAsync(
            "subscription-1",
            new SubscriptionMessage(new OrderPlaced("order-1", "alice"), 42, EventMetadata.Empty),
            new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken);

        var parked = (await store.ReadAllAsync()).Should().ContainSingle(e => e.Stream == "parked-subscription-1").Which;
        parked.EventType.Should().Be("$aggregates-parked");
        using var payload = JsonDocument.Parse(parked.Data);
        payload.RootElement.GetProperty("CommitPosition").GetUInt64().Should().Be(42);
        payload.RootElement.GetProperty("ExceptionType").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        payload.RootElement.GetProperty("Message").GetString().Should().Be("boom");
    }
}
