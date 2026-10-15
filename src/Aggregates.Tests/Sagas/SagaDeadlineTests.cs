using System.Runtime.CompilerServices;
using System.Text;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Sagas;

/// <summary>
/// Integration tests for saga deadlines: a saga requests a deadline, the scheduler delivers it back to
/// the same saga instance when it is due, and the saga chooses its commands from its current state
/// without changing it.
/// </summary>
[Trait("Category", "Integration")]
public class SagaDeadlineTests(ITestOutputHelper output) {
    const string Saga = "IntegrationTests.BillDeadlines@v1";
    static readonly Type[] StoredTypes = [typeof(BillPlanned), typeof(BillPaid), typeof(BillReminded), typeof(BillClosed), typeof(BillDue)];
    static readonly Type[] CommandTypes = [typeof(PlanBill), typeof(PayBill), typeof(RemindBill), typeof(CloseBill)];
    static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    static Task<TestHost> StartHostAsync(IStoreFixture store, ITestOutputHelper output, TimeProvider time, params Type[] failDeserializing) =>
        TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(CommandTypes)
            .FailDeserializing(failDeserializing)
            .Sagas(s => s
                .ScanTypes(typeof(BillDeadlineSaga))
                .WithResolver<IBillEvent>(e => [new AggregateIdentifier($"bill-deadlines-{e.BillId}")]))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

    // The entries of the saga stream of a bill, in stream order: the index is the revision.
    static async Task<IReadOnlyList<StoredEvent>> SagaStreamAsync(IStoreFixture store, string billId) =>
        [.. (await store.ReadAllAsync()).Where(e => e.Stream == $"bill-deadlines-{billId}").OrderBy(e => e.Position)];

    static async Task<int> DeadlineRequestsAsync(IStoreFixture store, string billId) =>
        (await SagaStreamAsync(store, billId)).Count(e => e.EventType == ScheduledFormat.DeadlineRequestEventType);

    static Task WaitForDeadlineRequestsAsync(IStoreFixture store, string billId, int count) =>
        Eventually.UntilAsync(async ValueTask<bool> () => await DeadlineRequestsAsync(store, billId) >= count,
            $"{count} deadline request(s) of {billId} are stored");

    static Task WaitForSagaStreamAsync(IStoreFixture store, string billId, int count) =>
        Eventually.UntilAsync(async ValueTask<bool> () => (await SagaStreamAsync(store, billId)).Count >= count,
            $"the saga stream of {billId} has {count} entries");

    // The events of a bill, deserialized, in order.
    static async Task<IReadOnlyList<object?>> EventsAsync(IStoreFixture store, TestHost host, string billId) =>
        [.. (await store.ReadAllAsync())
            .Where(e => e.Stream == billId)
            .OrderBy(e => e.Position)
            .Select(e => host.Serialization.Deserialize(e.EventType, e.Data))];

    static async Task<IReadOnlyList<string>> RemindersAsync(IStoreFixture store, TestHost host, string billId) =>
        [.. (await EventsAsync(store, host, billId)).OfType<BillReminded>().Select(reminder => reminder.Kind)];

    static async Task<int> DeliveriesAsync(IStoreFixture store) =>
        (await store.ReadAllAsync()).Count(e => e.Stream == ScheduledFormat.SchedulerStream(Saga) && e.EventType == ScheduledFormat.DeliveryEventType);

    static Task WaitForCaughtUpAsync(IStoreFixture store) =>
        Eventually.UntilAsync(
            async ValueTask<bool> () => (await store.ReadAllAsync()).Any(e =>
                e.Stream == ScheduledFormat.SchedulerStream(Saga)
                && e.EventType == ScheduledFormat.MarkerEventType
                && Encoding.UTF8.GetString(e.Data.Span) != "activated"),
            "the scheduler caught up with the store");

    // What the saga received for each deadline delivered to it, in order.
    static IReadOnlyList<DeliveredDeadline> Delivered(TestHost host) =>
        [.. host.Probe.Events<BillDeadlineSaga>().OfType<DeliveredDeadline>()];

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Deadline_IsDeliveredToTheSaga_WithItsPayloadAndCurrentState_WhenTheClockPassesIt(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(3)));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 1);
        await Eventually.QuietAsync();

        Delivered(host).Should().BeEmpty();
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host, "bill-1")).Count == 1,
            "the saga sends a reminder");

        Delivered(host).Should().ContainSingle().Which.Should().Be(
            new DeliveredDeadline(new DueState(Now.AddDays(3), false), new BillDue("bill-1", Now.AddDays(3), 1)));
        (await RemindersAsync(store, host, "bill-1")).Should().Equal("overdue-1");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Deadline_DoesNotChangeTheSagaState_NorWriteToTheSagaStream(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(1)));
        await host.SendAsync(new PayBill("bill-1"));
        await WaitForSagaStreamAsync(store, "bill-1", 3);
        time.Advance(TimeSpan.FromDays(1));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await SagaStreamAsync(store, "bill-1")).Count == 4,
            "the saga links the event of the command the deadline chose");

        // The last link is BillClosed: the delivery itself wrote nothing to the saga stream.
        var sagaStream = await SagaStreamAsync(store, "bill-1");
        sagaStream.Select(e => e.EventType).Should().Equal(
            EventReference.LinkEventType, ScheduledFormat.DeadlineRequestEventType, EventReference.LinkEventType, EventReference.LinkEventType);
        (await EventsAsync(store, host, "bill-1")).OfType<BillClosed>().Should().ContainSingle();
        (await RemindersAsync(store, host, "bill-1")).Should().BeEmpty();
        Delivered(host).Should().ContainSingle().Which.Should().Be(
            new DeliveredDeadline(new DueState(Now.AddDays(1), true), new BillDue("bill-1", Now.AddDays(1), 1)));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Deadline_ChoosesItsCommand_FromTheEventsBetweenRequestAndDue(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
        await host.SendAsync(new PlanBill("bill-2", Now.AddDays(2)));
        await host.SendAsync(new PayBill("bill-1"));
        await WaitForSagaStreamAsync(store, "bill-1", 3);
        await WaitForSagaStreamAsync(store, "bill-2", 2);
        time.Advance(TimeSpan.FromDays(2));
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await EventsAsync(store, host, "bill-1")).OfType<BillClosed>().Any() && (await RemindersAsync(store, host, "bill-2")).Count == 1,
            "bill-1 is closed and bill-2 gets a reminder");

        (await EventsAsync(store, host, "bill-1")).OfType<BillClosed>().Should().ContainSingle();
        (await RemindersAsync(store, host, "bill-1")).Should().BeEmpty();
        (await RemindersAsync(store, host, "bill-2")).Should().Equal("overdue-1");
        (await EventsAsync(store, host, "bill-2")).OfType<BillClosed>().Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task TwoOpenDeadlines_AreDeliveredInOrderOfDueTime_AndAStaleOneIsIgnored(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(4)));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 1);
        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 2);
        await host.SendAsync(new PayBill("bill-1"));
        await WaitForSagaStreamAsync(store, "bill-1", 5);
        time.Advance(TimeSpan.FromDays(5));
        await Eventually.UntilAsync(() => Delivered(host).Count == 2, "both deadlines are delivered");

        // The deadline of +4 days is stale: the bill's due date moved to +2 days, and it is paid.
        Delivered(host).Select(delivered => ((BillDue)delivered.Deadline).Due).Should().Equal(Now.AddDays(2), Now.AddDays(4));
        (await EventsAsync(store, host, "bill-1")).OfType<BillClosed>().Should().ContainSingle();
        (await RemindersAsync(store, host, "bill-1")).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeadlineInThePast_IsDeliveredImmediately(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(-1)));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 2);
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host, "bill-1")).Count == 2,
            "both reminders are sent without advancing the clock");

        // The second deadline falls on now: it runs right after the first.
        (await RemindersAsync(store, host, "bill-1")).Should().Equal("overdue-1", "overdue-2");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task AfterARestart_AnOpenDeadlineIsDelivered_AndADeliveredOneIsNot(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(1)));
            await first.SendAsync(new PlanBill("bill-2", Now.AddDays(5)));
            await first.SendAsync(new PayBill("bill-1"));
            await first.SendAsync(new PayBill("bill-2"));
            await WaitForSagaStreamAsync(store, "bill-1", 3);
            await WaitForSagaStreamAsync(store, "bill-2", 3);
            time.Advance(TimeSpan.FromDays(2));
            await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 1,
                "the deadline of bill-1 is delivered");
        }

        await using var second = await StartHostAsync(store, output, time);
        await WaitForCaughtUpAsync(store);
        await Eventually.QuietAsync();
        (await DeliveriesAsync(store)).Should().Be(1, "the delivered deadline is not delivered again");

        time.Advance(TimeSpan.FromDays(4));
        await Eventually.UntilAsync(async ValueTask<bool> () => await DeliveriesAsync(store) == 2,
            "the open deadline of bill-2 is delivered");
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await EventsAsync(store, second, "bill-2")).OfType<BillClosed>().Any(), "bill-2 is closed");

        (await EventsAsync(store, second, "bill-2")).OfType<BillClosed>().Should().ContainSingle();
        (await EventsAsync(store, second, "bill-1")).OfType<BillClosed>().Should().ContainSingle();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task FailedSagaCommit_LeavesNoDeadline(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        // No resolver: this host never handles events for the saga, so the test drives the commit by hand.
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(StoredTypes)
            .Commands(CommandTypes)
            .Sagas(s => s.ScanTypes(typeof(BillDeadlineSaga)))
            .Services(s => s.AddSingleton<TimeProvider>(time)));

        await store.AppendRawAsync("bill-deadlines-bill-1", "$>", "0@elsewhere"u8.ToArray());
        await store.AppendAsync(host.Serialization, "bill-1", new BillPlanned("bill-1", Now.AddDays(1)));

        await using var scope = host.Services.CreateAsyncScope();
        var planned = new BillPlanned("bill-1", Now.AddDays(1));
        var root = new SagaRoot<DueState, IBillEvent>(default, AggregateVersion.None);
        await root.AcceptAsync(planned, new EventReference("bill-1", 0), new BillDeadlineSaga(new HandlerProbe()), TestContext.Current.CancellationToken);
        var unitOfWork = new UnitOfWork();
        unitOfWork.Attach(new Aggregate("bill-deadlines-bill-1", root));

        var act = () => scope.ServiceProvider.GetRequiredService<SagaCommitDelegate>()(unitOfWork).AsTask();
        await act.Should().ThrowAsync<ConcurrencyException>();

        var sagaStream = await store.ReadAllAsync();
        sagaStream.Count(e => e.Stream == "bill-deadlines-bill-1").Should().Be(1, "only the hand-written link remains");
        sagaStream.Should().NotContain(e => e.EventType == ScheduledFormat.DeadlineRequestEventType, "the failed commit stored no deadline");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeadlineReaction_ThatRequestsANewDeadline_GetsItDeliveredToo(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(1)));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 1);
        time.Advance(TimeSpan.FromDays(1));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 2);
        time.Advance(TimeSpan.FromDays(1));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host, "bill-1")).Count == 2,
            "both reminders are sent");

        (await RemindersAsync(store, host, "bill-1")).Should().Equal("overdue-1", "overdue-2");
        var sagaStream = await SagaStreamAsync(store, "bill-1");
        var requests = sagaStream.Where(e => e.EventType == ScheduledFormat.DeadlineRequestEventType).ToList();
        requests.Should().HaveCount(2, "there is no level 3");
        var firstRevision = sagaStream.TakeWhile(e => e.EventType != ScheduledFormat.DeadlineRequestEventType).Count();
        ScheduledFormat.TryReadRequest(requests[1].Data, out var envelope).Should().BeTrue();
        envelope.Cause.Should().Be(new EventReference("bill-deadlines-bill-1", (ulong)firstRevision));
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task RedeliveredDeadline_DoesNotStoreItsRequestsTwice(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await host.SendAsync(new PlanBill("bill-1", Now.AddDays(1)));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 1);
        time.Advance(TimeSpan.FromDays(1));
        await WaitForDeadlineRequestsAsync(store, "bill-1", 2);
        var sagaStream = await SagaStreamAsync(store, "bill-1");
        var firstRevision = sagaStream.TakeWhile(e => e.EventType != ScheduledFormat.DeadlineRequestEventType).Count();

        // The same deadline delivered again, through the chain of the host.
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<LoggingSagaHandler<BillDeadlineSaga, DueState, IBillEvent>>();
        await handler.HandleDeadlineAsync("bill-deadlines-bill-1", new BillDue("bill-1", Now.AddDays(1), 1),
            new EventReference("bill-deadlines-bill-1", (ulong)firstRevision), TestContext.Current.CancellationToken);

        (await DeadlineRequestsAsync(store, "bill-1")).Should().Be(2);
        Delivered(host).Should().ContainSingle("the saga did not react again");
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task UndeserializableDeadline_IsParkedWhenDue(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        {
            await using var first = await StartHostAsync(store, output, time);
            await first.SendAsync(new PlanBill("bill-1", Now.AddDays(2)));
            await WaitForDeadlineRequestsAsync(store, "bill-1", 1);
        }

        await using var second = await StartHostAsync(store, output, time, typeof(BillDue));
        await WaitForCaughtUpAsync(store);
        time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await store.ReadAllAsync()).Count(e => e.Stream == $"parked-{ScheduledFormat.SchedulerStream(Saga)}") == 1,
            "the broken deadline is parked");

        (await RemindersAsync(store, second, "bill-1")).Should().BeEmpty();
        Delivered(second).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task DeadlineReaction_CarriesTheMetadataOfTheRequest(Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var time = new FakeTimeProvider(Now);
        await using var host = await StartHostAsync(store, output, time);

        await store.AppendAsync(host.Serialization, "bill-1", new BillPlanned("bill-1", Now.AddDays(-1)), ("correlation", "c-1"));
        await Eventually.UntilAsync(async ValueTask<bool> () => (await RemindersAsync(store, host, "bill-1")).Count == 2,
            "both reminders are sent");

        var reminded = (await store.ReadAllAsync())
            .Where(e => e.Stream == "bill-1" && host.Serialization.Deserialize(e.EventType, e.Data) is BillReminded)
            .ToList();
        reminded.Should().HaveCount(2);
        foreach (var entry in reminded)
            host.Serialization.DeserializeMetadata(entry.Metadata)["correlation"].Should().Be("c-1");
    }

    interface IBillDeadline;

    [EventContract("BillDue", @namespace: "IntegrationTests")]
    sealed record BillDue(string BillId, DateTimeOffset Due, int Level) : IBillDeadline;

    sealed record DueState(DateTimeOffset? Due, bool Paid) : IState<DueState, IBillEvent> {
        public static DueState Initial => new(null, false);
        public DueState Apply(IBillEvent @event) => @event switch {
            BillPlanned planned => this with { Due = planned.Due },
            BillPaid => this with { Paid = true },
            _ => this
        };
    }

    // What BillDeadlineSaga records in the probe when a deadline is delivered.
    sealed record DeliveredDeadline(DueState State, IBillDeadline Deadline);

    // On BillPlanned: a deadline at the due date. When it is due: a stale deadline does nothing, a paid
    // bill is closed, and an unpaid one gets an "overdue-{level}" reminder and, below level 2, a next
    // deadline a day later.
    [SagaContract("BillDeadlines", @namespace: "IntegrationTests")]
    sealed class BillDeadlineSaga(HandlerProbe probe) : ISaga<DueState, IBillEvent, IBillDeadline> {
        public async IAsyncEnumerable<ICommand> ReactAsync(DueState state, IBillEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (@event is BillPlanned planned)
                yield return Deadline.At(planned.Due, new BillDue(planned.BillId, planned.Due, 1));
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<ICommand> ReactToDeadlineAsync(DueState state, IBillDeadline deadline, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            probe.Record(this, new DeliveredDeadline(state, deadline));
            if (deadline is not BillDue due || state.Due != due.Due)
                yield break;
            if (state.Paid) {
                yield return new CloseBill(due.BillId);
                yield break;
            }
            yield return new RemindBill(due.BillId, due.Due, $"overdue-{due.Level}");
            if (due.Level < 2)
                yield return Deadline.At(due.Due.AddDays(due.Level), due with { Level = due.Level + 1 });
            await Task.CompletedTask;
        }
    }
}
