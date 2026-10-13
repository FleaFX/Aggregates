using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
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

    SubscriptionLoop BuildLoop(ScriptedSubscriptionFactory factory) =>
        new(factory, _store, _sink, _checkpointOptions, _resubscribeOptions, _time, _logger);

    static SubscriptionRegistration Registration(bool startFromEnd = false, params string[] predecessors) =>
        new(SubscriptionId, typeof(object), typeof(object), startFromEnd, predecessors);

    ValueTask Process(SubscriptionMessage message, CancellationToken cancellationToken) {
        lock (_processed) _processed.Add(message.CommitPosition);
        return ValueTask.CompletedTask;
    }

    // Runs the loop until the factory has played every script and the subscription is idle, then stops it.
    async Task RunUntilIdleAsync(ScriptedSubscriptionFactory factory, Func<SubscriptionMessage, CancellationToken, ValueTask>? process = null, SubscriptionRegistration? registration = null) {
        var idle = factory.Idle;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = BuildLoop(factory).RunAsync(registration ?? Registration(), process ?? Process, stopping.Token);

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
}
