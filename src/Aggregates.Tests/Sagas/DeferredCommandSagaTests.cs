using System.Runtime.CompilerServices;
using System.Text;
using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Sagas;

/// <summary>
/// Integration tests for deferred commands of sagas: a saga defers commands, the scheduler executes
/// them at their due time, and stale or broken ones are refused by the aggregate or parked.
/// </summary>
[Trait("Category", "Integration")]
public class DeferredCommandSagaTests(ITestOutputHelper output) {
    static readonly Type[] StoredTypes = [typeof(BillPlanned), typeof(BillPaid), typeof(BillReminded), typeof(RemindBill), typeof(FailBill)];
    static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    static Task<TestHost> StartHostAsync(IStoreFixture store, ITestOutputHelper output, TimeProvider time) =>
        TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .Sagas(s => s.ScanTypes(typeof(BillReminderSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

    static Task WaitForRequestsAsync(IStoreFixture store, int count) =>
        Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Count(e => e.EventType == ScheduledFormat.CommandRequestEventType) >= count,
            $"{count} deferred command request(s) are stored");

    static async Task<IReadOnlyList<string>> RemindersAsync(IStoreFixture store, TestHost host) =>
        [.. (await store.ReadAllAsync())
            .Where(e => e.Stream == "bill-1")
            .OrderBy(e => e.Position)
            .Select(e => host.Serialization.Deserialize(e.EventType, e.Data))
            .OfType<BillReminded>()
            .Select(reminder => reminder.Kind)];

    static async Task<int> DeliveriesAsync(IStoreFixture store, string saga = "IntegrationTests.BillReminders@v1") =>
        (await store.ReadAllAsync()).Count(e => e.Stream == ScheduledFormat.SchedulerStream(saga) && e.EventType == ScheduledFormat.DeliveryEventType);

    static Task WaitForCaughtUpAsync(IStoreFixture store, string saga = "IntegrationTests.BillReminders@v1") =>
        Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Any(e =>
                e.Stream == ScheduledFormat.SchedulerStream(saga)
                && e.EventType == ScheduledFormat.MarkerEventType
                && Encoding.UTF8.GetString(e.Data.Span) != "activated"),
            "the scheduler caught up with the store");

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task RequestedDeferredCommand_RunsItsCommand_WhenTheClockPassesIt(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(3)));
        await WaitForRequestsAsync(store, 2);
        await Eventually.QuietAsync();

        (await RemindersAsync(store, host)).Should().BeEmpty();
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host)).Count == 2,
            "both reminders are sent");
        (await RemindersAsync(store, host)).Should().BeEquivalentTo(["before", "due"]);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task TwoOpenDeferredCommands_RunInOrderOfDueTime(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
        await WaitForRequestsAsync(store, 2);
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host)).Count == 2,
            "both reminders are sent");

        // "due" was requested first, but "before" is due first: the scheduler runs them in order of due time.
        (await RemindersAsync(store, host)).Should().Equal("before", "due");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeferredCommandInThePast_RunsImmediately(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(-1)));

        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host)).Count == 2,
            "both reminders are sent without advancing the clock");
        (await RemindersAsync(store, host)).Should().BeEquivalentTo(["before", "due"]);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task AfterARestart_AnOpenDeferredCommandRuns_AndAHandledOneDoesNot(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(5)));
            await WaitForRequestsAsync(store, 2);
            time.Advance(TimeSpan.FromDays(4));
            await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 1,
                "the first reminder is delivered");
        }

        await using var second = await StartHostAsync(store, output, time);
        await WaitForCaughtUpAsync(store);
        await Eventually.QuietAsync();

        (await DeliveriesAsync(store)).Should().Be(1, "the handled deferred command does not run again");
        time.Advance(TimeSpan.FromDays(1));
        await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 2,
            "the open deferred command is delivered");
        (await RemindersAsync(store, second)).Should().Equal("before", "due");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task FailedSagaCommit_LeavesNoDeferredCommand(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        // The saga correlates no event: this host never handles events for it, so the test drives the commit by hand.
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .Sagas(s => s.ScanTypes(typeof(UnroutedBillReminderSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

        await store.AppendRawAsync("bill-reminders-bill-1", "$>", "0@elsewhere"u8.ToArray());
        await store.AppendAsync(host.Serialization, "bill-1", new BillPlanned("bill-1", Now.AddDays(1)));

        await using var scope = host.Services.CreateAsyncScope();
        var planned = new BillPlanned("bill-1", Now.AddDays(1));
        var root = new SagaRoot<ReminderState, IBillEvent>(default, AggregateVersion.None);
        await root.AcceptAsync(planned, new EventReference("bill-1", 0), new UnroutedBillReminderSaga(), TestContext.Current.CancellationToken);
        var unitOfWork = new UnitOfWork();
        unitOfWork.Attach(new Aggregate("bill-reminders-bill-1", root));

        var act = () => scope.ServiceProvider.GetRequiredService<SagaCommitDelegate>()(unitOfWork, TestContext.Current.CancellationToken).AsTask();
        await act.Should().ThrowAsync<ConcurrencyException>();

        var sagaStream = await store.ReadAllAsync();
        sagaStream.Count(e => e.Stream == "bill-reminders-bill-1").Should().Be(1, "only the hand-written link remains");
        sagaStream.Should().NotContain(e => e.EventType == ScheduledFormat.CommandRequestEventType, "the failed commit stored no deferred command");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task StaleDeferredCommands_AreRefusedByTheAggregate(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
        await WaitForRequestsAsync(store, 2);
        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(4)));
        await WaitForRequestsAsync(store, 4);
        time.Advance(TimeSpan.FromDays(5));
        await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 4,
            "all four deferred commands ran");
        await Eventually.QuietAsync();

        // The reminders planned for the first due date are stale: the bill's due date moved.
        var reminded = (await store.ReadAllAsync())
            .Where(e => e.Stream == "bill-1")
            .Select(e => host.Serialization.Deserialize(e.EventType, e.Data))
            .OfType<BillReminded>()
            .ToList();
        reminded.Should().HaveCount(2);
        reminded.Should().OnlyContain(reminder => reminder.Due == Now.AddDays(4));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeferredCommandEntries_ReachNoOtherSubscription(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .WithoutEventTypeRegistry()
            .Projections(typeof(BillProbeProjection))
            .Policies(typeof(BillProbePolicy))
            .Sagas(s => s.ScanTypes(typeof(BillReminderSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(-1)));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host)).Count == 2,
            "both reminders are sent");
        await Eventually.QuietAsync();

        host.Probe.Events<BillProbeProjection>().Should().HaveCount(3);
        host.Probe.Events<BillProbeProjection>().OfType<BillPlanned>().Should().HaveCount(1);
        host.Probe.Events<BillProbeProjection>().OfType<BillReminded>().Should().HaveCount(2);
        host.Probe.Events<BillProbePolicy>().Should().HaveCount(3);
        host.Probe.Events<BillProbePolicy>().OfType<BillPlanned>().Should().HaveCount(1);
        host.Probe.Events<BillProbePolicy>().OfType<BillReminded>().Should().HaveCount(2);
        host.Probe.DeserializeCalls.Keys.Should().NotContain(key => key.StartsWith("$"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task FailingCommand_IsParked_AndTheNextRuns(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .Sagas(s => s.ScanTypes(typeof(FailingBillReminderSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
        await WaitForRequestsAsync(store, 2);
        time.Advance(TimeSpan.FromDays(3));

        await Eventually.UntilAsync(
            async ValueTask<bool> () => {
                // The retries wait on the host's clock.
                time.Advance(TimeSpan.FromSeconds(1));
                return (await store.ReadAllAsync()).Count(e => e.Stream == "parked-scheduled-IntegrationTests.FailingBillReminders@v1") == 1;
            },
            "the failing deferred command is parked");
        // The deferred command after the failing one still runs, once the failing one is parked.
        await Eventually.UntilAsync(
            async ValueTask<bool> () => (await RemindersAsync(store, host)).Count == 1,
            "the deferred command after the failing one runs");
        await Eventually.QuietAsync();
        (await RemindersAsync(store, host)).Should().Equal(["due"]);
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task UndeserializableCommand_IsParkedWhenDue(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
            await WaitForRequestsAsync(store, 2);
        }

        await using var second = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .FailDeserializing(typeof(RemindBill))
            .Sagas(s => s.ScanTypes(typeof(BillReminderSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));
        await WaitForCaughtUpAsync(store);
        time.Advance(TimeSpan.FromDays(3));

        await Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Count(e => e.Stream == "parked-scheduled-IntegrationTests.BillReminders@v1") == 2,
            "both broken deferred commands are parked");
        (await RemindersAsync(store, second)).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ParkedDeferredCommands_AreReplayed_OnceTheyDeserialize(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
            await WaitForRequestsAsync(store, 2);
        }

        await using (var second = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .FailDeserializing(typeof(RemindBill))
            .Sagas(s => s.ScanTypes(typeof(BillReminderSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)))) {
            await WaitForCaughtUpAsync(store);
            time.Advance(TimeSpan.FromDays(3));
            await Eventually.UntilAsync(
                async ValueTask<bool> () => (await store.ReadAllAsync()).Count(e => e.Stream == "parked-scheduled-IntegrationTests.BillReminders@v1") == 2,
                "both broken deferred commands are parked");
        }

        await using var third = await StartHostAsync(store, output, time);
        await WaitForCaughtUpAsync(store);
        var parked = third.Services.GetRequiredService<IParkedMessages>();

        var replays = await parked.ReplayAllAsync("scheduled-IntegrationTests.BillReminders@v1", TestContext.Current.CancellationToken);

        replays.Should().HaveCount(2);
        replays.Should().OnlyContain(replay => replay.Succeeded);
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, third)).Count == 2,
            "the replayed reminders are sent");
        (await RemindersAsync(store, third)).Should().Equal("before", "due");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task UndeserializableCommand_RunsWhenFixedBeforeItIsDue(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
            await WaitForRequestsAsync(store, 2);
        }
        {
            await using var second = await TestHost.StartAsync(store, output, o => o
                .Events(StoredTypes)
                .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
                .FailDeserializing(typeof(RemindBill))
                .Sagas(s => s.ScanTypes(typeof(BillReminderSaga)))
                .Services(s => s.AddSingleton<TimeProvider>(time)));
            await WaitForCaughtUpAsync(store);
        }

        await using var third = await StartHostAsync(store, output, time);
        await WaitForCaughtUpAsync(store);
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, third)).Count == 2,
            "the repaired reminders are sent");

        (await RemindersAsync(store, third)).Should().Equal("before", "due");
        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream == "parked-scheduled-IntegrationTests.BillReminders@v1");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task HigherVersion_RunsTheDeferredCommandsOfItsPredecessor(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
            await WaitForRequestsAsync(store, 2);
        }

        await using var second = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(FailBill))
            .Sagas(s => s.ScanTypes(typeof(BillReminderSagaV2)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));
        await WaitForCaughtUpAsync(store, "IntegrationTests.BillReminders@v2");
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, second)).Count == 2,
            "the predecessor's deferred commands are delivered");

        (await RemindersAsync(store, second)).Should().Equal("before", "due");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SagaWithoutDeferredCommands_StartsNoScheduler(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Sagas(s => s.ScanTypes(typeof(SilentSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

        await store.AppendAsync(host.Serialization, "bill-1", new BillPlanned("bill-1", Now));
        await Eventually.UntilAsync(() => host.Probe.Count<SilentSaga>() == 1, "the silent saga handles the event");
        await Eventually.QuietAsync();

        (await store.ReadAllAsync()).Should().NotContain(e => e.Stream.StartsWith("scheduled-") || e.Stream.StartsWith("checkpoint-scheduled-"));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeferredCommand_CarriesTheMetadataOfTheRequest(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await store.AppendAsync(host.Serialization, "bill-1", new BillPlanned("bill-1", Now.AddDays(-1)), ("correlation", "c-1"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host)).Count == 2,
            "both reminders are sent");

        var reminded = (await store.ReadAllAsync())
            .Where(e => e.Stream == "bill-1" && host.Serialization.Deserialize(e.EventType, e.Data) is BillReminded)
            .ToList();
        reminded.Should().HaveCount(2);
        foreach (var entry in reminded)
            host.Serialization.DeserializeMetadata(entry.Metadata)["correlation"].Should().Be("c-1");
    }

    sealed record ReminderState(int Seen) : IState<ReminderState, IBillEvent> {
        public static ReminderState Initial => new(0);
        public ReminderState Apply(IBillEvent @event) => this with { Seen = Seen + 1 };
    }

    [SagaContract("BillReminders", @namespace: "IntegrationTests")]
    sealed class BillReminderSaga : ISaga<ReminderState, IBillEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IBillEvent @event, EventMetadata metadata) =>
            [new AggregateIdentifier($"bill-reminders-{@event.BillId}")];

        public IAsyncEnumerable<ICommand> ReactAsync(ReminderState state, IBillEvent @event, CancellationToken cancellationToken = default) =>
            Reminders.Plan(@event);
    }

    [SagaContract("UnroutedBillReminders", @namespace: "IntegrationTests")]
    sealed class UnroutedBillReminderSaga : ISaga<ReminderState, IBillEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IBillEvent @event, EventMetadata metadata) => [];

        public IAsyncEnumerable<ICommand> ReactAsync(ReminderState state, IBillEvent @event, CancellationToken cancellationToken = default) =>
            Reminders.Plan(@event);
    }

    [SagaContract("BillReminders", version: 2, @namespace: "IntegrationTests")]
    sealed class BillReminderSagaV2 : ISaga<ReminderState, IBillEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IBillEvent @event, EventMetadata metadata) =>
            [new AggregateIdentifier($"bill-reminders-{@event.BillId}")];

        public IAsyncEnumerable<ICommand> ReactAsync(ReminderState state, IBillEvent @event, CancellationToken cancellationToken = default) =>
            Reminders.Plan(@event);
    }

    [SagaContract("FailingBillReminders", @namespace: "IntegrationTests")]
    sealed class FailingBillReminderSaga : ISaga<ReminderState, IBillEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IBillEvent @event, EventMetadata metadata) =>
            [new AggregateIdentifier($"failing-reminders-{@event.BillId}")];

        public IAsyncEnumerable<ICommand> ReactAsync(ReminderState state, IBillEvent @event, CancellationToken cancellationToken = default) =>
            FailingReminders.Plan(@event);
    }

    [SagaContract("Silent", @namespace: "IntegrationTests")]
    sealed class SilentSaga(HandlerProbe probe) : ISaga<ReminderState, IBillEvent> {
        public IEnumerable<AggregateIdentifier> Correlate(IBillEvent @event, EventMetadata metadata) =>
            [new AggregateIdentifier($"silent-{@event.BillId}")];

        public async IAsyncEnumerable<ICommand> ReactAsync(ReminderState state, IBillEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }

    static class Reminders {
        // "due" first, "before" second: the scheduler must still run them in order of due time.
        public static async IAsyncEnumerable<ICommand> Plan(IBillEvent @event) {
            if (@event is BillPlanned planned) {
                yield return new RemindBill(planned.BillId, planned.Due, "due").DeferUntil(planned.Due);
                yield return new RemindBill(planned.BillId, planned.Due, "before").DeferUntil(planned.Due.AddDays(-1));
            }
            await Task.CompletedTask;
        }
    }

    static class FailingReminders {
        public static async IAsyncEnumerable<ICommand> Plan(IBillEvent @event) {
            if (@event is BillPlanned planned) {
                yield return new FailBill(planned.BillId).DeferUntil(planned.Due.AddDays(-1));
                yield return new RemindBill(planned.BillId, planned.Due, "due").DeferUntil(planned.Due);
            }
            await Task.CompletedTask;
        }
    }

    [ProjectionContract("BillProbeProjection", @namespace: "IntegrationTests")]
    sealed class BillProbeProjection(HandlerProbe probe) : IProjection<IBillEvent> {
        public ValueTask<ICommit> ProjectAsync(IBillEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }

    [PolicyContract("BillProbePolicy", @namespace: "IntegrationTests")]
    sealed class BillProbePolicy(HandlerProbe probe) : IPolicy<IBillEvent> {
        public async IAsyncEnumerable<ICommand> ReactAsync(IBillEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            yield break;
        }
    }
}
