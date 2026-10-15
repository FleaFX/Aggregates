using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

[Trait("Category", "Integration")]
public class ScheduledStoreTests(ITestOutputHelper output) {
    static readonly DateTimeOffset Due = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task TryAppendRequests_ToANewStream_StoresEveryRequest_AndToAnExistingOne_ReturnsFalse(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(typeof(Ping)).Sagas(_ => { }));
        var scheduled = host.Services.GetRequiredService<IScheduledStore>();
        const string stream = "scheduled-requests-Tests.Policy@v1-3@source-1";
        DeferredCommandRequest[] requests = [new("Tests.Policy@v1", Due, new Ping("a")), new("Tests.Policy@v1", Due.AddDays(1), new Ping("b"))];

        var first = await scheduled.TryAppendRequestsAsync(stream, requests, TestContext.Current.CancellationToken);
        var second = await scheduled.TryAppendRequestsAsync(stream, [new("Tests.Policy@v1", Due, new Ping("c"))], TestContext.Current.CancellationToken);

        first.Should().BeTrue();
        second.Should().BeFalse("the stream exists already");
        var stored = (await store.ReadAllAsync()).Where(e => e.Stream == stream).ToArray();
        stored.Should().HaveCount(2).And.OnlyContain(e => e.EventType == ScheduledFormat.CommandRequestEventType);
        var commands = stored.Select(e => {
            ScheduledFormat.TryReadRequest(e.Data, out var envelope).Should().BeTrue();
            envelope.Owner.Should().Be("Tests.Policy@v1");
            return scheduled.Deserialize(envelope.Type, envelope.Data);
        });
        commands.Should().Equal(new Ping("a"), new Ping("b"));
    }

    sealed record Ping(string Id) : ICommand;
}
