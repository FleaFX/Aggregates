using System.Collections.Concurrent;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Sagas;

public class SagaTimeoutSchedulerTests {
    const string Owner = "Test.Reminders@v1";
    static readonly DateTimeOffset Start = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    static readonly SubscriptionRegistration Registration = new(Owner, typeof(TestSaga), typeof(TestEvent), false);

    readonly FakeTimeProvider _time = new(Start);
    readonly FakeSagaTimeoutStore _store = new() { Deserializer = (type, _) => type == "Test.Command@v1" ? new TestCommand() : null };
    readonly FakeLogger<SagaTimeoutScheduler> _logger = new();
    readonly InMemoryCheckpointStore _checkpointStore = new();
    readonly ISubscriptionFactory _factory = A.Fake<ISubscriptionFactory>();

    public SagaTimeoutSchedulerTests() {
        A.CallTo(() => _factory.IsTransient(A<Exception>._)).Returns(true);
    }

    SagaTimeoutActivation<TestSaga> Activated() {
        // The activation marker: the saga requested its first timeout in a previous run (or process).
        _store.Append(SagaTimeouts.SubscriptionId(Owner), SagaTimeoutFormat.MarkerEventType, "activated"u8.ToArray());
        return new(_store, [Registration]);
    }

    SagaTimeoutScheduler Scheduler(ISubscriptionFactory? factory = null, FakeLogger<SagaTimeoutScheduler>? logger = null) =>
        new(_store, _checkpointStore, factory ?? _factory,
            new SubscriptionCheckpointOptions { MaxBatchSize = 1 },
            new SubscriptionResubscribeOptions { InitialDelay = TimeSpan.FromSeconds(1) },
            _time, logger ?? _logger);

    static ValueTask DeliverAsync(ConcurrentQueue<OpenSagaTimeout> delivered, OpenSagaTimeout timeout, CancellationToken cancellationToken) {
        delivered.Enqueue(timeout);
        return ValueTask.CompletedTask;
    }

    Task CaughtUpAsync(FakeLogger<SagaTimeoutScheduler> logger) =>
        Eventually.UntilAsync(() => logger.Collector.GetSnapshot().Any(record => record.Message.Contains("caught up")),
            "the scheduler caught up");

    [Fact]
    public async Task DeliversATimeout_OnlyWhenTheClockPassesIt() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(3));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await Eventually.QuietAsync();

        delivered.Should().BeEmpty();
        _time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered");

        delivered.Should().ContainSingle().Which.Command.Should().BeOfType<TestCommand>();
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task DeliversTimeoutsInOrderOfDueTime() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(2));
        _store.AppendRequest("saga-1", Owner, Start.AddDays(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        _time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(() => delivered.Count == 2, "both timeouts are delivered");

        delivered.Select(timeout => timeout.Due).Should().Equal(Start.AddDays(1), Start.AddDays(2));
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task DeliversATimeoutInThePast_RightAway() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);

        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered right away");
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task HandledTimeouts_AreNotDeliveredAgain() {
        var activation = Activated();
        for (var i = 0; i < 50; i++) {
            var request = _store.AppendRequest($"saga-{i}", Owner, Start.AddDays(-1));
            _store.AppendDelivery(SagaTimeouts.SubscriptionId(Owner), request.Source);
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await Eventually.QuietAsync();

        delivered.Should().BeEmpty();
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RecordsEachDelivery_InItsStream() {
        var activation = Activated();
        var request = _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered");

        var delivery = _store.Entries.Should().ContainSingle(entry =>
            entry.EventType == SagaTimeoutFormat.DeliveryEventType && entry.Source.StreamId == SagaTimeouts.SubscriptionId(Owner)).Which;
        delivery.Data.ToArray().Should().Equal(request.Source.ToLinkData());
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task IgnoresTheTimeoutsOfOtherSagas() {
        var activation = Activated();
        _store.AppendRequest("saga-1", "Test.Other@v1", Start.AddDays(-1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await Eventually.QuietAsync();

        delivered.Should().BeEmpty();
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task DeliversTheTimeoutsOfAPredecessor() {
        var registration = new SubscriptionRegistration("Test.Reminders@v2", typeof(TestSaga), typeof(TestEvent), false, ["Test.Reminders@v1"]);
        _store.Append(SagaTimeouts.SubscriptionId("Test.Reminders@v2"), SagaTimeoutFormat.MarkerEventType, "activated"u8.ToArray());
        var activation = new SagaTimeoutActivation<TestSaga>(_store, [registration]);
        _store.AppendRequest("saga-1", "Test.Reminders@v1", Start.AddDays(-1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);

        await Eventually.UntilAsync(() => delivered.Count == 1, "the predecessor's timeout is delivered");
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Checkpoint_StaysBeforeTheOldestOpenTimeout() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(1));
        _store.AppendRequest("saga-1", Owner, Start.AddDays(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        _time.Advance(TimeSpan.FromDays(2));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the first timeout is delivered");
        await cts.CancelAsync();
        await run;

        (await _checkpointStore.GetAsync(SagaTimeouts.SubscriptionId(Owner), TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task Checkpoint_FollowsTheMarkers_WhenNothingIsOpen() {
        var activation = Activated();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await cts.CancelAsync();
        await run;

        (await _checkpointStore.GetAsync(SagaTimeouts.SubscriptionId(Owner), TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task StartsAfterItsCheckpoint() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        await _checkpointStore.StoreAsync(SagaTimeouts.SubscriptionId(Owner), 2, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout after the checkpoint is delivered");

        delivered.Should().ContainSingle().Which.Request.Revision.Should().Be(1);
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task ContinuesFromAPredecessorsCheckpoint_AndAdoptsIt() {
        var registration = new SubscriptionRegistration("Test.Reminders@v2", typeof(TestSaga), typeof(TestEvent), false, ["Test.Reminders@v1"]);
        _store.Append(SagaTimeouts.SubscriptionId("Test.Reminders@v2"), SagaTimeoutFormat.MarkerEventType, "activated"u8.ToArray());
        var activation = new SagaTimeoutActivation<TestSaga>(_store, [registration]);
        await _checkpointStore.StoreAsync(SagaTimeouts.SubscriptionId("Test.Reminders@v1"), 7, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await cts.CancelAsync();
        await run;

        _checkpointStore.Stored.Should().Contain((SagaTimeouts.SubscriptionId("Test.Reminders@v2"), 7));
    }

    [Fact]
    public async Task WaitsUntilActivated() {
        _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        var activation = new SagaTimeoutActivation<TestSaga>(_store, [Registration]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await Eventually.QuietAsync();

        _store.Subscriptions.Should().Be(0);
        delivered.Should().BeEmpty();

        await activation.EnsureActivatedAsync(TestContext.Current.CancellationToken);
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered after the saga is activated");

        _store.Subscriptions.Should().BeGreaterThan(0);
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task SubscribesAgain_AfterATransientFailure() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        _store.FailNextSubscribe = new InvalidOperationException("connection lost");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);

        await Eventually.UntilAsync(() => _store.Subscriptions == 1, "the first subscription starts");
        _time.Advance(TimeSpan.FromSeconds(2));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered after resubscribing");

        _store.Subscriptions.Should().Be(2);
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Stops_AfterAFailureThatIsNotTransient() {
        var factory = A.Fake<ISubscriptionFactory>();
        A.CallTo(() => factory.IsTransient(A<Exception>._)).Returns(false);
        var activation = Activated();
        _store.FailNextSubscribe = new InvalidOperationException("not allowed");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler(factory).RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);

        var act = () => run;
        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task RetriesADelivery_AfterATransientFailure() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(-1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();
        var attempts = 0;

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => {
            if (Interlocked.Exchange(ref attempts, attempts + 1) == 0)
                throw new InvalidOperationException("transient failure");
            delivered.Enqueue(timeout);
            return ValueTask.CompletedTask;
        }, cts.Token);

        await Eventually.UntilAsync(() => attempts == 1, "the first delivery attempt fails");
        _time.Advance(TimeSpan.FromSeconds(2));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the delivery is retried");

        delivered.Should().ContainSingle();
        _store.Entries.Should().ContainSingle(entry =>
            entry.EventType == SagaTimeoutFormat.DeliveryEventType && entry.Source.StreamId == SagaTimeouts.SubscriptionId(Owner));
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task DeliversATimeoutDueInMonths() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(90));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        _time.Advance(TimeSpan.FromDays(89));
        await Eventually.QuietAsync();

        delivered.Should().BeEmpty();
        _time.Advance(TimeSpan.FromDays(2));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered");

        delivered.Should().ContainSingle().Which.Command.Should().BeOfType<TestCommand>();
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task DeliversAfterAClockJump() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await Eventually.QuietAsync();

        delivered.Should().BeEmpty();
        _time.SetUtcNow(Start.AddDays(31));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the timeout is delivered after the clock jump");
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task WarnsRightAway_AboutACommandThatCannotBeDeserialized() {
        var activation = Activated();
        _store.AppendRequest("saga-1", Owner, Start.AddDays(30), commandType: "Test.Unknown@v1");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);

        _logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Level == LogLevel.Warning && record.Message.Contains("can't deserialize the command"));
        await Eventually.QuietAsync();
        delivered.Should().BeEmpty();

        _time.Advance(TimeSpan.FromDays(30));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the broken timeout is handed to the deliverer when it is due");

        delivered.Should().ContainSingle().Which.Error.Should().NotBeNull();
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task ARepairedCommand_IsDeliveredAfterARestart() {
        var activation = Activated();
        var request = _store.AppendRequest("saga-1", Owner, Start.AddDays(2));
        _store.Deserializer = (_, _) => null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new ConcurrentQueue<OpenSagaTimeout>();

        var run = Scheduler().RunAsync(Registration, activation, (timeout, _) => DeliverAsync(delivered, timeout, _), cts.Token);
        await CaughtUpAsync(_logger);
        await cts.CancelAsync();
        await run;

        _logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Level == LogLevel.Warning && record.Message.Contains("can't deserialize the command"));

        // Restart: the same store and checkpoint store, a deserializer that knows the command again.
        _store.Deserializer = (type, _) => type == "Test.Command@v1" ? new TestCommand() : null;
        var restartedLogger = new FakeLogger<SagaTimeoutScheduler>();
        using var restart = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var second = Scheduler(logger: restartedLogger).RunAsync(Registration,
            new SagaTimeoutActivation<TestSaga>(_store, [Registration]), (timeout, _) => DeliverAsync(delivered, timeout, _), restart.Token);
        await CaughtUpAsync(restartedLogger);
        _time.Advance(TimeSpan.FromDays(3));
        await Eventually.UntilAsync(() => delivered.Count == 1, "the repaired timeout is delivered");

        delivered.Should().ContainSingle().Which.Command.Should().BeOfType<TestCommand>();
        delivered.Should().ContainSingle().Which.Request.Should().Be(request.Source);
        await restart.CancelAsync();
        await second;
    }

    sealed class InMemoryCheckpointStore : ICheckpointStore {
        readonly object _lock = new();
        readonly Dictionary<string, ulong> _checkpoints = [];

        public List<(string Id, ulong Position)> Stored { get; } = [];

        public ValueTask<ulong?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default) {
            lock (_lock)
                return ValueTask.FromResult(_checkpoints.TryGetValue(subscriptionId, out var position) ? (ulong?)position : null);
        }

        public ValueTask StoreAsync(string subscriptionId, ulong position, CancellationToken cancellationToken = default) {
            lock (_lock) {
                _checkpoints[subscriptionId] = position;
                Stored.Add((subscriptionId, position));
            }
            return ValueTask.CompletedTask;
        }
    }
}
