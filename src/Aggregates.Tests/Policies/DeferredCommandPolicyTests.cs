using System.Runtime.CompilerServices;
using System.Text;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Policies;

/// <summary>
/// Integration tests for deferred commands of policies: a policy defers a command, its requests are
/// stored once per event, and the scheduler executes them at their due time.
/// </summary>
[Trait("Category", "Integration")]
public class DeferredCommandPolicyTests(ITestOutputHelper output) {
    const string FollowUp = "IntegrationTests.BillFollowUp@v1";
    static readonly Type[] StoredTypes = [typeof(BillPlanned), typeof(BillPaid), typeof(BillReminded), typeof(RemindBill), typeof(FailBill)];
    static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    static Task<TestHost> StartHostAsync(IStoreFixture store, ITestOutputHelper output, TimeProvider time, params Type[] policies) =>
        TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .Policies(policies.Length == 0 ? [typeof(BillFollowUpPolicy)] : policies)
            .Services(s => s.AddSingleton<TimeProvider>(time)));

    static async Task<int> RequestsAsync(IStoreFixture store) =>
        (await store.ReadAllAsync()).Count(e => e.EventType == ScheduledFormat.CommandRequestEventType);

    static Task WaitForRequestsAsync(IStoreFixture store, int count) =>
        Eventually.UntilAsync(async ValueTask<bool> () => await RequestsAsync(store) >= count,
            $"{count} deferred command request(s) are stored");

    static async Task<IReadOnlyList<string>> RemindedBillsAsync(IStoreFixture store, TestHost host) =>
        [.. (await store.ReadAllAsync())
            .OrderBy(e => e.Position)
            .Select(e => host.Serialization.Deserialize(e.EventType, e.Data))
            .OfType<BillReminded>()
            .Select(reminded => reminded.BillId)];

    static async Task<int> DeliveriesAsync(IStoreFixture store, string policy = FollowUp) =>
        (await store.ReadAllAsync()).Count(e => e.Stream == ScheduledFormat.SchedulerStream(policy) && e.EventType == ScheduledFormat.DeliveryEventType);

    static Task WaitForCaughtUpAsync(IStoreFixture store, string policy = FollowUp) =>
        Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Any(e =>
                e.Stream == ScheduledFormat.SchedulerStream(policy)
                && e.EventType == ScheduledFormat.MarkerEventType
                && Encoding.UTF8.GetString(e.Data.Span) != "activated"),
            "the scheduler caught up with the store");

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeferredCommand_RunsWhenTheClockPassesIt(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(3)));
        await WaitForRequestsAsync(store, 1);
        await Eventually.QuietAsync();

        (await RemindedBillsAsync(store, host)).Should().BeEmpty();
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindedBillsAsync(store, host)).Count == 1,
            "the reminder is sent");
        (await store.ReadAllAsync()).Should().Contain(e => e.Stream.StartsWith($"scheduled-requests-{FollowUp}-"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeferredCommandInThePast_RunsImmediately(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(-1)));

        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindedBillsAsync(store, host)).Count == 1,
            "the reminder is sent without advancing the clock");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task AfterARestart_AnOpenRequestRuns_AndADeliveredOneDoesNot(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(1)));
            await first.SendAsync(new PlanBill("bill-2", Now.AddDays(5)));
            await WaitForRequestsAsync(store, 2);
            time.Advance(TimeSpan.FromDays(2));
            await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 1,
                "the first reminder is delivered");
        }

        await using var second = await StartHostAsync(store, output, time);
        await WaitForCaughtUpAsync(store);
        await Eventually.QuietAsync();

        (await DeliveriesAsync(store)).Should().Be(1, "the delivered request does not run again");
        time.Advance(TimeSpan.FromDays(4));
        await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 2,
            "the open request is delivered");
        (await RemindedBillsAsync(store, second)).Should().Equal("bill-1", "bill-2");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ReprocessedEvent_StoresItsRequestsOnce_AndRunsTheCommandOnce(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);
        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
        await WaitForRequestsAsync(store, 1);

        // The same event again, as after a retry or a restart before the checkpoint.
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LoggingPolicyHandler<BillFollowUpPolicy, IBillEvent>>()
                .HandleAsync(new BillPlanned("bill-1", Now.AddDays(2)), new EventReference("bill-1", 0), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) >= 1, "the request is delivered");
        await Eventually.QuietAsync();

        (await RequestsAsync(store)).Should().Be(1, "the second reaction finds the request stream of the event");
        (await DeliveriesAsync(store)).Should().Be(1);
        (await RemindedBillsAsync(store, host)).Should().Equal("bill-1");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task FailingImmediateCommand_LeavesNoRequest(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time, typeof(FailingFollowUpPolicy));

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(-1)));
        await Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Any(e => e.Stream == "parked-IntegrationTests.FailingFollowUp@v1"),
            "the event is parked");
        await Eventually.QuietAsync();

        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("scheduled-requests-"));
        (await RemindedBillsAsync(store, host)).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task PolicyWithoutDeferredCommands_StartsNoScheduler(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time, typeof(PayingPolicy));

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(1)));
        await Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Any(e => host.Serialization.Deserialize(e.EventType, e.Data) is BillPaid),
            "the policy pays the bill");
        await Eventually.QuietAsync();

        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("scheduled-") || e.Stream.StartsWith("checkpoint-scheduled-"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task HigherVersion_RunsTheRequestsOfItsPredecessor(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
            await WaitForRequestsAsync(store, 1);
        }

        await using var second = await StartHostAsync(store, output, time, typeof(BillFollowUpPolicyV2));
        await WaitForCaughtUpAsync(store, "IntegrationTests.BillFollowUp@v2");
        time.Advance(TimeSpan.FromDays(3));

        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindedBillsAsync(store, second)).Count == 1,
            "the predecessor's request is delivered");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeferredCommand_CarriesTheMetadataOfTheTriggerEvent(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await store.AppendAsync(host.Serialization, "bill-1", new BillPlanned("bill-1", Now.AddDays(-1)), ("correlation", "c-1"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindedBillsAsync(store, host)).Count == 1,
            "the reminder is sent");

        var reminded = (await store.ReadAllAsync())
            .Single(e => e.Stream == "bill-1" && host.Serialization.Deserialize(e.EventType, e.Data) is BillReminded);
        host.Serialization.DeserializeMetadata(reminded.Metadata)["correlation"].Should().Be("c-1");
    }

    static async IAsyncEnumerable<ICommand> RemindWhenDue(IBillEvent @event) {
        if (@event is BillPlanned planned)
            yield return new RemindBill(planned.BillId, planned.Due, "due").DeferUntil(planned.Due);
        await Task.CompletedTask;
    }

    [PolicyContract("BillFollowUp", @namespace: "IntegrationTests")]
    sealed class BillFollowUpPolicy : IPolicy<IBillEvent> {
        public IAsyncEnumerable<ICommand> ReactAsync(IBillEvent @event, CancellationToken cancellationToken = default) =>
            RemindWhenDue(@event);
    }

    [PolicyContract("BillFollowUp", version: 2, @namespace: "IntegrationTests")]
    sealed class BillFollowUpPolicyV2 : IPolicy<IBillEvent> {
        public IAsyncEnumerable<ICommand> ReactAsync(IBillEvent @event, CancellationToken cancellationToken = default) =>
            RemindWhenDue(@event);
    }

    // Defers a reminder, then dispatches a command that always fails.
    [PolicyContract("FailingFollowUp", @namespace: "IntegrationTests")]
    sealed class FailingFollowUpPolicy : IPolicy<IBillEvent> {
        public async IAsyncEnumerable<ICommand> ReactAsync(IBillEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (@event is not BillPlanned planned)
                yield break;
            yield return new RemindBill(planned.BillId, planned.Due, "due").DeferUntil(planned.Due);
            yield return new FailBill(planned.BillId);
            await Task.CompletedTask;
        }
    }

    // Only immediate commands.
    [PolicyContract("Paying", @namespace: "IntegrationTests")]
    sealed class PayingPolicy : IPolicy<IBillEvent> {
        public async IAsyncEnumerable<ICommand> ReactAsync(IBillEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (@event is BillPlanned planned)
                yield return new PayBill(planned.BillId);
            await Task.CompletedTask;
        }
    }
}
