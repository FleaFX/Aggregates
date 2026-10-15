using Aggregates.Projections;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using static Aggregates.Testing.ScriptedSubscriptionFactory;

namespace Aggregates.Subscriptions;

public class SubscriptionLoopTests {
    const string SubscriptionId = "sub-1";
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly IParkedMessageSink _sink = A.Fake<IParkedMessageSink>();
    readonly FakeTimeProvider _time = new();
    readonly FakeLogger<SubscriptionLoop> _logger = new();
    readonly List<ulong> _processed = [];

    // Checkpoints are only written by the final flush, unless a test lowers the batch size.
    SubscriptionCheckpointOptions _checkpointOptions = new() { MaxBatchSize = 1000, MaxInterval = TimeSpan.FromHours(1) };

    // No delay between attempts, unless a test is about the backoff.
    SubscriptionResubscribeOptions _resubscribeOptions = new() { InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };

    static CancellationToken Token => TestContext.Current.CancellationToken;

    SubscriptionLoop BuildLoop(ScriptedSubscriptionFactory factory, EventTypeRegistry? registry = null) =>
        new(factory, _store, _sink, _checkpointOptions, _resubscribeOptions, _time, _logger, registry);

    static SubscriptionRegistration Registration(bool startFromEnd = false, params string[] predecessors) =>
        new(SubscriptionId, typeof(object), typeof(object), startFromEnd, predecessors);

    ValueTask Process(SubscriptionMessage message, CancellationToken cancellationToken) {
        lock (_processed) _processed.Add(message.CommitPosition);
        return ValueTask.CompletedTask;
    }

    // Runs the loop until the factory has played every script and the subscription is idle, then stops it.
    async Task RunUntilIdleAsync(ScriptedSubscriptionFactory factory, Func<SubscriptionMessage, CancellationToken, ValueTask>? process = null, SubscriptionRegistration? registration = null, EventTypeRegistry? registry = null) {
        var idle = factory.Idle;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = BuildLoop(factory, registry).RunAsync(registration ?? Registration(), process ?? Process, stopping.Token);

        await idle.WaitAsync(Timeout, Token);
        await stopping.CancelAsync();
        await run.WaitAsync(Timeout, Token);
    }

    IReadOnlyList<FakeLogRecord> Logs(LogLevel level) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Level == level)];

    static string? State(FakeLogRecord record, string key) =>
        record.StructuredState!.Single(entry => entry.Key == key).Value;

    public class Resubscribe : SubscriptionLoopTests {
        [Fact]
        public async Task GivenFailureMidStream_SubscribesAgainAfterLastProcessedMessage() {
            var factory = new ScriptedSubscriptionFactory()
                .ThenFail(new IOException("connection lost"), Message(1), Message(2), Message(3))
                .Then(Message(4), Message(5));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(null, 3UL);
            _processed.Should().Equal(1UL, 2UL, 3UL, 4UL, 5UL);
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenStoredCheckpoint_FirstSubscriptionStartsAfterIt() {
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).Returns(41UL);
            var factory = new ScriptedSubscriptionFactory().Then(Message(42));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(41UL);
        }

        [Fact]
        public async Task GivenFailureBeforeFirstMessage_SubscribesAgainAfterStoredCheckpoint() {
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).Returns(41UL);
            var factory = new ScriptedSubscriptionFactory()
                .ThenFail(new IOException("connection lost"))
                .Then(Message(42));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(41UL, 41UL);
            _processed.Should().Equal(42UL);
        }

        [Fact]
        public async Task GivenSubscribeThrows_TriesAgain() {
            var factory = new ScriptedSubscriptionFactory()
                .ThenFailToSubscribe(new IOException("unreachable"))
                .Then(Message(1));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().HaveCount(2);
            _processed.Should().Equal(1UL);
        }

        [Fact]
        public async Task GivenCheckpointReadFails_TriesAgain() {
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._))
                .Throws(new IOException("unreachable")).Once()
                .Then.Returns(7UL);
            var factory = new ScriptedSubscriptionFactory().Then(Message(8));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(7UL);
            _processed.Should().Equal(8UL);
        }

        [Fact]
        public async Task GivenCheckpointWriteFails_SubscribesAgainAfterLastProcessedMessage_AndWritesItLater() {
            _checkpointOptions = new SubscriptionCheckpointOptions { MaxBatchSize = 2, MaxInterval = TimeSpan.FromHours(1) };
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 2UL, A<CancellationToken>._))
                .Throws(new IOException("unreachable")).Once();
            var factory = new ScriptedSubscriptionFactory()
                .Then(Message(1), Message(2))
                .Then(Message(3));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(null, 2UL);
            _processed.Should().Equal(1UL, 2UL, 3UL);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 3UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenSubscriptionEndsWithoutFailure_SubscribesAgain_AndLogsWarning() {
            var factory = new ScriptedSubscriptionFactory()
                .ThenEnd(Message(1))
                .Then(Message(2));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(null, 1UL);
            _processed.Should().Equal(1UL, 2UL);
            Logs(LogLevel.Warning).Should().ContainSingle(record => record.Message.Contains("ended unexpectedly"));
        }

        [Fact]
        public async Task LogsEachResubscribe_WithSubscriptionIdAndPosition() {
            var failure = new IOException("connection lost");
            var factory = new ScriptedSubscriptionFactory()
                .ThenFail(failure, Message(1), Message(2))
                .Then(Message(3));

            await RunUntilIdleAsync(factory);

            var warning = Logs(LogLevel.Warning).Single();
            warning.Exception.Should().BeSameAs(failure);
            State(warning, "SubscriptionId").Should().Be(SubscriptionId);
            State(warning, "Position").Should().Be("2");
            State(warning, "Attempt").Should().Be("1");
            var recovered = Logs(LogLevel.Information).Single(record => record.Message.Contains("again"));
            State(recovered, "SubscriptionId").Should().Be(SubscriptionId);
            State(recovered, "Position").Should().Be("2");
        }

        [Fact]
        public async Task GivenFiveConsecutiveFailures_LogsErrorFromTheFifth() {
            var factory = new ScriptedSubscriptionFactory();
            for (var i = 0; i < 6; i++)
                factory.ThenFailToSubscribe(new IOException("unreachable"));
            factory.Then(Message(1));

            await RunUntilIdleAsync(factory);

            Logs(LogLevel.Warning).Should().HaveCount(4);
            Logs(LogLevel.Error).Select(record => State(record, "Attempt")).Should().Equal("5", "6");
        }
    }

    public class ResubscribeDelay : SubscriptionLoopTests {
        [Fact]
        public async Task GrowsExponentially_CappedAtMaxDelay_AndStartsOverAfterProcessedMessage() {
            _resubscribeOptions = new SubscriptionResubscribeOptions {
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(4),
                BackoffMultiplier = 2.0
            };
            var factory = new ScriptedSubscriptionFactory();
            for (var i = 0; i < 4; i++)
                factory.ThenFailToSubscribe(new IOException("unreachable"));
            factory.ThenFail(new IOException("connection lost"), Message(1));
            factory.Then(Message(2));
            var idle = factory.Idle;
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);

            var run = BuildLoop(factory).RunAsync(Registration(), Process, stopping.Token);
            while (!idle.IsCompleted) {
                _time.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(10, Token);
            }
            await stopping.CancelAsync();
            await run.WaitAsync(Timeout, Token);

            var delays = Logs(LogLevel.Warning).Select(record => TimeSpan.Parse(State(record, "Delay")!).TotalSeconds).ToArray();
            delays.Should().HaveCount(5);
            delays[0].Should().BeApproximately(1, 0.1);
            delays[1].Should().BeApproximately(2, 0.2);
            delays[2].Should().BeApproximately(4, 0.4);
            delays[3].Should().BeApproximately(4, 0.4, "the delay is capped at MaxDelay");
            delays[4].Should().BeApproximately(1, 0.1, "a processed message starts the backoff over");
        }
    }

    public class Stopping : SubscriptionLoopTests {
        [Fact]
        public async Task GivenCancellationDuringStream_EndsWithoutException_AndWritesLastPosition() {
            var factory = new ScriptedSubscriptionFactory().Then(Message(1), Message(2), Message(3));

            await RunUntilIdleAsync(factory);

            A.CallTo(() => _store.StoreAsync(SubscriptionId, 3UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenCancellationDuringBackoff_EndsWithoutException_AndWritesLastPosition() {
            _resubscribeOptions = new SubscriptionResubscribeOptions { InitialDelay = TimeSpan.FromMinutes(1) };
            var factory = new ScriptedSubscriptionFactory().ThenFail(new IOException("connection lost"), Message(1));
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);

            var run = BuildLoop(factory).RunAsync(Registration(), Process, stopping.Token);
            while (Logs(LogLevel.Warning).Count == 0)
                await Task.Delay(10, Token);
            await stopping.CancelAsync();

            await run.WaitAsync(Timeout, Token);
            factory.FromPositions.Should().HaveCount(1);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenFinalFlushFails_EndsWithoutException_AndLogsError() {
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._)).Throws(new IOException("unreachable"));
            var factory = new ScriptedSubscriptionFactory().Then(Message(1));

            await RunUntilIdleAsync(factory);

            var error = Logs(LogLevel.Error).Single();
            error.Exception.Should().BeOfType<IOException>();
            State(error, "Position").Should().Be("1");
        }

        [Fact]
        public async Task GivenFailureThatIsNotTransient_Rethrows_AndWritesLastPosition() {
            var failure = new UnauthorizedAccessException("denied");
            var factory = new ScriptedSubscriptionFactory(isTransient: e => e is not UnauthorizedAccessException)
                .ThenFail(failure, Message(1));

            var act = () => BuildLoop(factory).RunAsync(Registration(), Process, Token);

            (await act.Should().ThrowAsync<UnauthorizedAccessException>()).Which.Should().BeSameAs(failure);
            factory.FromPositions.Should().HaveCount(1);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            Logs(LogLevel.Critical).Should().ContainSingle().Which.Exception.Should().BeSameAs(failure);
        }

        [Fact]
        public async Task GivenFailureThatIsNotTransient_AndFinalFlushFails_RethrowsOriginalFailure() {
            var failure = new UnauthorizedAccessException("denied");
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._)).Throws(new IOException("unreachable"));
            var factory = new ScriptedSubscriptionFactory(isTransient: e => e is not UnauthorizedAccessException)
                .ThenFail(failure, Message(1));

            var act = () => BuildLoop(factory).RunAsync(Registration(), Process, Token);

            (await act.Should().ThrowAsync<UnauthorizedAccessException>()).Which.Should().BeSameAs(failure);
            Logs(LogLevel.Error).Should().ContainSingle().Which.Exception.Should().BeOfType<IOException>();
        }
    }

    public class Messages : SubscriptionLoopTests {
        [Fact]
        public async Task GivenDeserializationError_ParksMessageOnce_WithoutProcessing() {
            var error = new InvalidOperationException("poison");
            var factory = new ScriptedSubscriptionFactory().Then(Undeserializable(1, error), Message(2));

            await RunUntilIdleAsync(factory);

            _processed.Should().Equal(2UL);
            A.CallTo(() => _sink.ParkAsync(SubscriptionId, A<SubscriptionMessage>.That.Matches(m => m.CommitPosition == 1), error, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 2UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            Logs(LogLevel.Warning).Should().ContainSingle().Which.Exception.Should().BeSameAs(error);
        }

        [Fact]
        public async Task GivenUnknownEventType_ProcessesMessage_WithoutParking() {
            var factory = new ScriptedSubscriptionFactory().Then(new SubscriptionMessage(null, 1, new EventReference("stream-1", 1), EventMetadata.Empty));

            await RunUntilIdleAsync(factory);

            _processed.Should().Equal(1UL);
            A.CallTo(() => _sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenParkingFails_SubscribesAgainBeforeTheMessage() {
            var error = new InvalidOperationException("poison");
            A.CallTo(() => _sink.ParkAsync(SubscriptionId, A<SubscriptionMessage>._, error, A<CancellationToken>._))
                .Throws(new IOException("unreachable")).Once();
            var factory = new ScriptedSubscriptionFactory()
                .Then(Message(1), Undeserializable(2, error))
                .Then(Undeserializable(2, error), Message(3));

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(null, 1UL);
            _processed.Should().Equal(1UL, 3UL);
            A.CallTo(() => _sink.ParkAsync(SubscriptionId, A<SubscriptionMessage>._, error, A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
        }

        [Fact]
        public async Task GivenProcessingFails_SubscribesAgainBeforeTheMessage() {
            var attempts = 0;
            var factory = new ScriptedSubscriptionFactory()
                .Then(Message(1), Message(2))
                .Then(Message(2));

            await RunUntilIdleAsync(factory, (message, ct) => {
                if (message.CommitPosition == 2 && attempts++ == 0)
                    throw new IOException("parked-message store unreachable");
                return Process(message, ct);
            });

            factory.FromPositions.Should().Equal(null, 1UL);
            _processed.Should().Equal(1UL, 2UL);
        }

        [Fact]
        public async Task GivenBlockAndDeserializationError_NeitherParksNorProcesses_AndSubscribesAgainBeforeTheMessage() {
            var error = new InvalidOperationException("poison");
            var factory = new ScriptedSubscriptionFactory()
                .Then(Message(1), Undeserializable(2, error), Message(3))
                .Then(Message(2), Message(3));

            await RunUntilIdleAsync(factory, registration: Registration() with { ErrorMode = SubscriptionErrorMode.Block });

            A.CallTo(() => _sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            _processed.Should().Equal(1UL, 2UL, 3UL);
            factory.FromPositions.Should().Equal(null, 1UL);
            var blocked = Logs(LogLevel.Error).Should().ContainSingle().Which;
            blocked.Message.Should().Contain("blocks instead of parking");
            blocked.Exception.Should().BeSameAs(error);
        }

        [Fact]
        public async Task GivenBlockAndDeserializationErrorOnTheFirstMessage_SubscribesAgainFromTheCheckpoint() {
            var error = new InvalidOperationException("poison");
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).Returns(10UL);
            var factory = new ScriptedSubscriptionFactory()
                .Then(Undeserializable(11, error))
                .Then(Message(11));

            await RunUntilIdleAsync(factory, registration: Registration() with { ErrorMode = SubscriptionErrorMode.Block });

            factory.FromPositions.Should().Equal(10UL, 10UL);
            _processed.Should().Equal(11UL);
        }
    }
    public class StartPosition : SubscriptionLoopTests {
        [Fact]
        public async Task GivenItsOwnCheckpoint_OnlyReadsItsOwnCheckpoint() {
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).Returns(41UL);
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(false, "sub-1@v2", "sub-1@v1"));

            factory.FromPositions.Should().Equal(41UL);
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => _store.GetAsync("sub-1@v1", A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
            Logs(LogLevel.Warning).Should().BeEmpty();
        }

        [Fact]
        public async Task WithoutItsOwnCheckpoint_AdoptsTheNearestPredecessorCheckpoint_BeforeSubscribing() {
            const ulong P = 41;
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).Returns(P);
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(false, "sub-1@v2", "sub-1@v1"));

            factory.FromPositions.Should().Equal(P);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, P, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.GetAsync("sub-1@v1", A<CancellationToken>._)).MustNotHaveHappened();
            var information = Logs(LogLevel.Information).Single(record => record.Message.Contains("continuing from"));
            State(information, "SubscriptionId").Should().Be(SubscriptionId);
            State(information, "Predecessor").Should().Be("sub-1@v2");
            State(information, "Position").Should().Be("41");
        }

        [Fact]
        public async Task WithoutItsOwnCheckpoint_SkipsPredecessorsWithoutACheckpoint() {
            const ulong P = 41;
            A.CallTo(() => _store.GetAsync("sub-1@v1", A<CancellationToken>._)).Returns(P);
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(false, "sub-1@v2", "sub-1@v1"));

            factory.FromPositions.Should().Equal(P);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, P, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            var warning = Logs(LogLevel.Warning).Single();
            State(warning, "Predecessor").Should().Be("sub-1@v1");
            State(warning, "Skipped").Should().Be("sub-1@v2");
        }

        [Fact]
        public async Task GivenBothPredecessorsHaveACheckpoint_AdoptsTheNearest() {
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).Returns(41UL);
            A.CallTo(() => _store.GetAsync("sub-1@v1", A<CancellationToken>._)).Returns(7UL);
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(false, "sub-1@v2", "sub-1@v1"));

            factory.FromPositions.Should().Equal(41UL);
            A.CallTo(() => _store.GetAsync("sub-1@v1", A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 41UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenAPredecessorCheckpoint_ItWinsOverStartFromEnd() {
            const ulong P = 41;
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).Returns(P);
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(true, "sub-1@v2"));

            factory.FromPositions.Should().Equal(P);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, P, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task WithoutAnyPredecessorCheckpoint_StartsFromTheEnd_AndWarns() {
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(true, "sub-1@v2", "sub-1@v1"));

            factory.FromPositions.Should().ContainSingle().Which.Should().BeNull();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
            var warning = Logs(LogLevel.Warning).Single();
            State(warning, "Predecessors").Should().Be("sub-1@v2, sub-1@v1");
            State(warning, "Start").Should().Be("end");
        }

        [Fact]
        public async Task WithoutPredecessors_ReadsNoOtherCheckpoint() {
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration());

            factory.FromPositions.Should().ContainSingle().Which.Should().BeNull();
            A.CallTo(() => _store.GetAsync(A<string>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            Logs(LogLevel.Warning).Should().BeEmpty();
        }

        [Fact]
        public async Task GivenTheAdoptedCheckpointCannotBeWritten_SubscribesOnlyAfterASuccessfulWrite() {
            const ulong P = 41;
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).Returns(P);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, A<ulong>._, A<CancellationToken>._))
                .Throws(new IOException("store down")).Once();
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(false, "sub-1@v2"));

            factory.FromPositions.Should().Equal(P);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, P, A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
        }

        [Fact]
        public async Task GivenAResubscribe_DoesNotReadThePredecessorAgain() {
            const ulong P = 41;
            ulong? checkpoint = null;
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).ReturnsLazily(() => checkpoint);
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).Returns(P);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, P, A<CancellationToken>._))
                .Invokes(() => checkpoint = P);
            var factory = new ScriptedSubscriptionFactory()
                .ThenFailToSubscribe(new IOException("lost"))
                .Then();

            await RunUntilIdleAsync(factory, registration: Registration(false, "sub-1@v2"));

            factory.FromPositions.Should().Equal(P, P);
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
    }

    public class EventTypes : SubscriptionLoopTests {
        static readonly EventTypeRegistry Registry = EventTypeRegistry.FromTypes(
            [typeof(FilterA), typeof(FilterB), typeof(FilterC)]);

        static SubscriptionRegistration OnFilterEvent(bool startFromEnd = false) =>
            new(SubscriptionId, typeof(object), typeof(IFilterEvent), startFromEnd);

        [Fact]
        public async Task WithoutRegistry_SubscribesWithoutEventTypes() {
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: OnFilterEvent());

            factory.Requests.Should().ContainSingle().Which.EventTypes.Should().BeNull();
        }

        [Fact]
        public async Task GivenAnObjectType_SubscribesWithoutEventTypes() {
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(), registry: Registry);

            factory.Requests.Should().ContainSingle().Which.EventTypes.Should().BeNull();
        }

        [Fact]
        public async Task GivenAnEventType_SubscribesWithItsNames() {
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: OnFilterEvent(), registry: Registry);

            factory.Requests.Should().ContainSingle().Which.EventTypes
                .Should().Equal("Tests.Loop.A@v1", "Tests.Loop.B@v1");
        }

        [Fact]
        public async Task GivenNoMatchingEventType_SubscribesWithoutEventTypes_AndWarns() {
            var registry = EventTypeRegistry.FromTypes([typeof(FilterC)]);
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: OnFilterEvent(), registry: registry);

            factory.Requests.Should().ContainSingle().Which.EventTypes.Should().BeNull();
            var warning = Logs(LogLevel.Warning).Single();
            State(warning, "SubscriptionId").Should().Be(SubscriptionId);
            State(warning, "EventType").Should().Be(typeof(IFilterEvent).FullName);
        }

        [Fact]
        public async Task GivenEventTypes_LogsTheCount_AndAllWithoutThem() {
            var filtered = new ScriptedSubscriptionFactory().Then();
            await RunUntilIdleAsync(filtered, registration: OnFilterEvent(), registry: Registry);
            var subscribing = Logs(LogLevel.Information).Single(record => record.Message.Contains("subscribing"));
            State(subscribing, "EventTypes").Should().Be("2");

            _logger.Collector.Clear();
            var unfiltered = new ScriptedSubscriptionFactory().Then();
            await RunUntilIdleAsync(unfiltered, registration: Registration(), registry: Registry);
            subscribing = Logs(LogLevel.Information).Single(record => record.Message.Contains("subscribing"));
            State(subscribing, "EventTypes").Should().Be("all");
        }

        [Fact]
        public async Task GivenAResubscribe_ReusesTheSameNames() {
            var factory = new ScriptedSubscriptionFactory()
                .ThenFail(new IOException("lost"), Message(1))
                .Then(Message(2));

            await RunUntilIdleAsync(factory, registration: OnFilterEvent(), registry: Registry);

            factory.Requests.Should().HaveCount(2);
            factory.Requests[0].EventTypes.Should().Equal("Tests.Loop.A@v1", "Tests.Loop.B@v1");
            factory.Requests[1].EventTypes.Should().Equal("Tests.Loop.A@v1", "Tests.Loop.B@v1");
            Logs(LogLevel.Debug).Count(record => record.Message.Contains("receives the event types")).Should().Be(1);
        }
    }

    public class Progress : SubscriptionLoopTests {
        [Fact]
        public async Task GivenProgress_DoesNotProcessIt_AndWritesTheCheckpointRightAway() {
            var factory = new ScriptedSubscriptionFactory().Then(SubscriptionMessage.Progress(5), Message(6));

            await RunUntilIdleAsync(factory);

            _processed.Should().Equal(6UL);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 5UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenProgressAtZero_WithoutFromPosition_WritesNothing() {
            var factory = new ScriptedSubscriptionFactory().Then(SubscriptionMessage.Progress(0));

            await RunUntilIdleAsync(factory);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenProgressBehindTheLastProcessedPosition_WritesOnlyThatPosition() {
            var factory = new ScriptedSubscriptionFactory().Then(Message(5), SubscriptionMessage.Progress(4));

            await RunUntilIdleAsync(factory);

            A.CallTo(() => _store.StoreAsync(SubscriptionId, 4UL, A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 5UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenAFailure_SubscribesAgainAfterTheLastProgress() {
            var factory = new ScriptedSubscriptionFactory()
                .ThenFail(new IOException("lost"), SubscriptionMessage.Progress(5))
                .Then();

            await RunUntilIdleAsync(factory);

            factory.FromPositions.Should().Equal(null, 5UL);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 5UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenProgressAfterAFailure_LogsThatItReceivesMessagesAgain() {
            var factory = new ScriptedSubscriptionFactory()
                .ThenFail(new IOException("lost"))
                .Then(SubscriptionMessage.Progress(5));

            await RunUntilIdleAsync(factory);

            Logs(LogLevel.Information).Should().ContainSingle(record => record.Message.Contains("receives messages again"));
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 5UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
    }

    public class StartFromEnd : SubscriptionLoopTests {
        [Fact]
        public async Task WithoutACheckpoint_StoresTheEndPosition_BeforeSubscribingAfterIt() {
            var factory = new ScriptedSubscriptionFactory().Then();
            factory.EndPosition = 42;

            await RunUntilIdleAsync(factory, registration: Registration(startFromEnd: true));

            factory.FromPositions.Should().Equal(42UL);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 42UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            var information = Logs(LogLevel.Information).Single(record => record.Message.Contains("starting from the end"));
            State(information, "Position").Should().Be("42");
        }

        [Fact]
        public async Task GivenAFailingSubscribe_UsesTheStoredEndPositionAgain() {
            ulong? checkpoint = null;
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).ReturnsLazily(() => checkpoint);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 42UL, A<CancellationToken>._)).Invokes(() => checkpoint = 42);
            var factory = new ScriptedSubscriptionFactory()
                .ThenFailToSubscribe(new IOException("lost"))
                .Then();
            factory.EndPosition = 42;

            await RunUntilIdleAsync(factory, registration: Registration(startFromEnd: true));

            factory.FromPositions.Should().Equal(42UL, 42UL);
            factory.EndPositionCalls.Should().Be(1);
        }

        [Fact]
        public async Task GivenNoEndPosition_StartsFromTheEndWithoutWriting() {
            var factory = new ScriptedSubscriptionFactory().Then();

            await RunUntilIdleAsync(factory, registration: Registration(startFromEnd: true));

            var request = factory.Requests.Should().ContainSingle().Which;
            request.FromPosition.Should().BeNull();
            request.StartFromEnd.Should().BeTrue();
            request.EventTypes.Should().BeNull();
            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenItsOwnCheckpoint_DoesNotAskForTheEndPosition() {
            A.CallTo(() => _store.GetAsync(SubscriptionId, A<CancellationToken>._)).Returns(41UL);
            var factory = new ScriptedSubscriptionFactory().Then();
            factory.EndPosition = 42;

            await RunUntilIdleAsync(factory, registration: Registration(startFromEnd: true));

            factory.FromPositions.Should().Equal(41UL);
            factory.EndPositionCalls.Should().Be(0);
        }

        [Fact]
        public async Task GivenAPredecessorCheckpoint_DoesNotAskForTheEndPosition() {
            A.CallTo(() => _store.GetAsync("sub-1@v2", A<CancellationToken>._)).Returns(41UL);
            var factory = new ScriptedSubscriptionFactory().Then();
            factory.EndPosition = 42;

            await RunUntilIdleAsync(factory, registration: Registration(true, "sub-1@v2"));

            factory.FromPositions.Should().Equal(41UL);
            factory.EndPositionCalls.Should().Be(0);
        }

        [Fact]
        public async Task GivenStartFromEndFalse_DoesNotAskForTheEndPosition() {
            var factory = new ScriptedSubscriptionFactory().Then();
            factory.EndPosition = 42;

            await RunUntilIdleAsync(factory, registration: Registration());

            factory.EndPositionCalls.Should().Be(0);
        }
    }

    public class DependencyInjection : SubscriptionLoopTests {
        [Fact]
        public void GivenNoEventTypeRegistry_ResolvesTheLoop() {
            var services = new ServiceCollection();
            services.AddProjections(o => o.ScanTypes());
            services.AddSingleton(A.Fake<ISubscriptionFactory>());
            services.AddSingleton(A.Fake<ICheckpointStore>());
            services.AddLogging();

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<SubscriptionLoop>().Should().NotBeNull();
        }

        [Fact]
        public void GivenAnEventTypeRegistry_ResolvesTheLoop() {
            var services = new ServiceCollection();
            services.AddProjections(o => o.ScanTypes());
            services.AddSingleton(A.Fake<ISubscriptionFactory>());
            services.AddSingleton(A.Fake<ICheckpointStore>());
            services.AddSingleton(EventTypeRegistry.FromTypes([typeof(FilterA)]));
            services.AddLogging();

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<SubscriptionLoop>().Should().NotBeNull();
        }
    }
}

interface IFilterEvent;

[EventContract("A", @namespace: "Tests.Loop")]
sealed record FilterA : IFilterEvent;

[EventContract("B", @namespace: "Tests.Loop")]
sealed record FilterB : IFilterEvent;

[EventContract("C", @namespace: "Tests.Loop")]
sealed record FilterC;
