using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aggregates.Sagas;

public class SagaSubscriptionServiceTests {
    const string SubscriptionId = "sub-1";
    static readonly AggregateIdentifier SagaId = new("saga-1");

    readonly ICommandDispatcher _commandDispatcher = A.Fake<ICommandDispatcher>();
    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly IParkedMessageSink _parkedMessageSink = A.Fake<IParkedMessageSink>();
    readonly ISagaRepository<TestSagaState, TestEvent> _repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
    readonly ISaga<TestSagaState, TestEvent, TestDeadline> _saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
    readonly FakeScheduledStore _scheduled = new() { Deserializer = (type, _) => type switch { "Test.Command@v1" => new TestCommand(), "Test.Deadline@v1" => new TestDeadline(1), _ => null } };

    // Park on the first failure, so the tests don't wait for retries.
    SubscriptionErrorHandlingOptions _errorOptions = new() { MaxRetries = 1 };

    public SagaSubscriptionServiceTests() {
        A.CallTo(() => _saga.Correlate(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId]);
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
    }

    static CancellationToken Token => TestContext.Current.CancellationToken;

    // The interface itself as TSaga, so the saga can be faked behind the real handler chain.
    SagaSubscriptionService<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildService(FakeSubscriptionFactory factory, SubscriptionErrorMode errorMode = SubscriptionErrorMode.Park) {
        var registration = new SubscriptionRegistration(SubscriptionId, typeof(TestSaga), typeof(TestEvent), StartFromEnd: false) { ErrorMode = errorMode };
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ISaga<TestSagaState, TestEvent>>(_saga)
            .AddSingleton(_repository)
            .AddSingleton(_commandDispatcher)
            .AddSingleton<SagaCommitDelegate>(_ => ValueTask.CompletedTask)
            .AddSingleton<IScheduledStore>(_scheduled)
            .AddSingleton(typeof(SchedulerActivation<>))
            .AddScoped(typeof(LoggingSagaHandler<,,>))
            .AddScoped(typeof(RetrySagaHandler<,,>))
            .AddScoped(typeof(UnitOfWorkAwareSagaHandler<,,>))
            .AddScoped(typeof(SagaHandler<,,>));
        var provider = services.BuildServiceProvider();
        var retryPolicy = new SubscriptionRetryPolicy(_parkedMessageSink, _errorOptions);
        return new(new SubscriptionLoop(factory, _store, _parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            provider.GetRequiredService<IServiceScopeFactory>(),
            retryPolicy,
            new Scheduler(_scheduled, _store, factory, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), provider.GetRequiredService<IServiceScopeFactory>(), retryPolicy,
                _parkedMessageSink, TimeProvider.System, NullLoggerFactory.Instance),
            new SchedulerActivation<ISaga<TestSagaState, TestEvent>>(_scheduled, [registration]),
            registration);
    }

    // The repository is loaded once per saga id the event is handled for.
    FakeItEasy.Configuration.IReturnValueArgumentValidationConfiguration<ValueTask<SagaRoot<TestSagaState, TestEvent>?>> HandledFor(AggregateIdentifier sagaId) =>
        A.CallTo(() => _repository.TryGetAsync(sagaId, A<CancellationToken>._));

    IEnumerable<ulong> StoredPositions() =>
        Fake.GetCalls(_store)
            .Where(call => call.Method.Name == nameof(ICheckpointStore.StoreAsync))
            .Select(call => call.GetArgument<ulong>(1));

    [Fact]
    public async Task GivenOneMatchingEventAmongOthers_HandlesItOnce_AndStoresCheckpointsInBatches() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(251,
            position => position == 1 ? new TestEvent(1) : new OtherTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        HandledFor(SagaId).MustHaveHappenedOnceExactly();
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, new TestEvent(1), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        StoredPositions().Should().Equal(100UL, 200UL, 251UL);
    }

    [Fact]
    public async Task GivenStopped_StoresLastProcessedPosition() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(3,
            position => new OtherTestEvent((int)position)));
        using var service = BuildService(factory);
        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);

        await service.StopAsync(Token);

        StoredPositions().Should().Equal(3UL);
    }

    [Fact]
    public async Task GivenMatchingEvent_PassesItsSourceToTheSaga() {
        // The fake assigns each message the source "stream-1" at its commit position.
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new TestEvent(1)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        A.CallTo(() => _repository.Add(A<AggregateIdentifier>._, A<SagaRoot<TestSagaState, TestEvent>>.That.Matches(
                root => root.GetChanges().OfType<EventReference>().Single().StreamId == "stream-1")))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GivenCorrelateFails_ParksEvent_AndContinues() {
        var failure = new InvalidOperationException("correlate failed");
        A.CallTo(() => _saga.Correlate(new TestEvent(3), A<EventMetadata>._)).Throws(failure);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(5,
            position => new TestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        A.CallTo(() => _parkedMessageSink.ParkAsync(SubscriptionId, A<SubscriptionMessage>.That.Matches(m => m.CommitPosition == 3), failure, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        HandledFor(SagaId).MustHaveHappened(4, Times.Exactly);
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, new TestEvent(3), A<CancellationToken>._)).MustNotHaveHappened();
        StoredPositions().Should().Equal(5UL);
    }

    [Fact]
    public async Task GivenCorrelateReturnsAnIdStartingWithDollar_ParksEvent_AndContinues() {
        A.CallTo(() => _saga.Correlate(new TestEvent(3), A<EventMetadata>._)).ReturnsLazily(() => new[] { new AggregateIdentifier("$saga-3") });
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(5,
            position => new TestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        A.CallTo(() => _parkedMessageSink.ParkAsync(SubscriptionId, A<SubscriptionMessage>.That.Matches(m => m.CommitPosition == 3), A<ArgumentException>.That.Matches(e => e.Message.Contains("'$'")), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        HandledFor(SagaId).MustHaveHappened(4, Times.Exactly);
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, new TestEvent(3), A<CancellationToken>._)).MustNotHaveHappened();
        StoredPositions().Should().Equal(5UL);
    }

    [Fact]
    public async Task GivenSecondSagaFails_DoesNotRunFirstSagaAgain() {
        _errorOptions = new SubscriptionErrorHandlingOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var secondSagaId = new AggregateIdentifier("saga-2");
        A.CallTo(() => _saga.Correlate(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId, secondSagaId]);
        HandledFor(secondSagaId).Throws(new InvalidOperationException("saga failed"));
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new TestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        HandledFor(secondSagaId).MustHaveHappened(3, Times.Exactly);
        HandledFor(SagaId).MustHaveHappenedOnceExactly();
        A.CallTo(() => _saga.Correlate(A<TestEvent>._, A<EventMetadata>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ExecutesAnOpenDeferredCommand_ByDispatchingItsCommand() {
        _scheduled.Append("scheduled-sub-1", ScheduledFormat.MarkerEventType, "activated"u8.ToArray());
        _scheduled.AppendRequest("saga-1", SubscriptionId, DateTimeOffset.UtcNow.AddDays(-1));
        var dispatched = false;
        A.CallTo(() => _commandDispatcher.DispatchAsync(A<ICommand>.That.IsInstanceOf(typeof(TestCommand)), A<CancellationToken>._))
            .Invokes(_ => dispatched = true);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new OtherTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await Eventually.UntilAsync(() => dispatched, "the deferred command is dispatched");
        await service.StopAsync(Token);

        A.CallTo(() => _commandDispatcher.DispatchAsync(A<ICommand>.That.IsInstanceOf(typeof(TestCommand)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DeliversADueDeadline_ToTheSagaOfItsStream_WithItsCurrentState() {
        _scheduled.Append("scheduled-sub-1", ScheduledFormat.MarkerEventType, "activated"u8.ToArray());
        _scheduled.AppendDeadline("saga-7", SubscriptionId, DateTimeOffset.UtcNow.AddDays(-1));
        var command = new TestCommand();
        var handled = false;
        TestSagaState? capturedState = null;
        A.CallTo(() => _repository.TryGetAsync(new AggregateIdentifier("saga-7"), A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(4), new AggregateVersion(1))));
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Invokes(call => capturedState = call.GetArgument<TestSagaState>(0))
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());
        A.CallTo(() => _commandDispatcher.DispatchAsync(command, A<CancellationToken>._))
            .Invokes(_ => handled = true);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new OtherTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await Eventually.UntilAsync(() => handled, "the saga dispatches the command of its deadline reaction");
        await service.StopAsync(Token);

        HandledFor(new AggregateIdentifier("saga-7")).MustHaveHappenedOnceExactly();
        capturedState.Should().Be(new TestSagaState(4));
        A.CallTo(() => _commandDispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AFailingDeadlineReaction_IsParkedUnderTheScheduler() {
        _scheduled.Append("scheduled-sub-1", ScheduledFormat.MarkerEventType, "activated"u8.ToArray());
        _scheduled.AppendDeadline("saga-7", SubscriptionId, DateTimeOffset.UtcNow.AddDays(-1));
        var failure = new InvalidOperationException("deadline failed");
        A.CallTo(() => _repository.TryGetAsync(new AggregateIdentifier("saga-7"), A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(4), new AggregateVersion(1))));
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Throws(failure);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new OtherTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);

        await Eventually.UntilAsync(() => Fake.GetCalls(_parkedMessageSink).Any(call =>
            call.Method.Name == nameof(IParkedMessageSink.ParkAsync)), "the failing deadline reaction is parked");
        await service.StopAsync(Token);

        A.CallTo(() => _parkedMessageSink.ParkAsync(ScheduledFormat.SchedulerStream(SubscriptionId), A<SubscriptionMessage>._, failure, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_HandlesTheEventForEverySagaItResolvesTo() {
        var secondSagaId = new AggregateIdentifier("saga-2");
        A.CallTo(() => _saga.Correlate(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId, secondSagaId]);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new TestEvent(1)));
        using var service = BuildService(factory);

        await service.HandleAsync(new SubscriptionMessage(new TestEvent(1), 42, new EventReference("stream-1", 1), EventMetadata.Empty), Token);

        HandledFor(SagaId).MustHaveHappenedOnceExactly();
        HandledFor(secondSagaId).MustHaveHappenedOnceExactly();
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, new TestEvent(1), A<CancellationToken>._)).MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task HandleAsync_DoesNotRetryOrPark_WhenASagaFails() {
        _errorOptions = new SubscriptionErrorHandlingOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var failure = new InvalidOperationException("saga failed");
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._)).Throws(failure);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new TestEvent(1)));
        using var service = BuildService(factory);

        var act = () => service.HandleAsync(new SubscriptionMessage(new TestEvent(1), 42, new EventReference("stream-1", 1), EventMetadata.Empty), Token).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _parkedMessageSink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task HandleDeadlineAsync_DeliversTheDeadlineToTheSagaOfItsStream() {
        var due = DateTimeOffset.UtcNow.AddDays(-1);
        var command = new TestCommand();
        TestSagaState? capturedState = null;
        A.CallTo(() => _repository.TryGetAsync(new AggregateIdentifier("saga-7"), A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(4), new AggregateVersion(1))));
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Invokes(call => capturedState = call.GetArgument<TestSagaState>(0))
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new OtherTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.HandleDeadlineAsync(new ScheduledDeadline(new EventReference("saga-7", 2), due, new TestDeadline(1), EventMetadata.Empty, 5), Token);

        HandledFor(new AggregateIdentifier("saga-7")).MustHaveHappenedOnceExactly();
        capturedState.Should().Be(new TestSagaState(4));
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, new TestDeadline(1), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _commandDispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GivenBlock_RetriesAFailingSaga_InsteadOfParkingIt() {
        _errorOptions = new SubscriptionErrorHandlingOptions { MaxRetries = 1, InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var failure = new InvalidOperationException("saga failed");
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._)).Throws(failure).NumberOfTimes(3);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new TestEvent(1)));
        using var service = BuildService(factory, SubscriptionErrorMode.Block);

        await service.StartAsync(Token);
        await Eventually.UntilAsync(() => Fake.GetCalls(_saga).Count(call => call.Method.Name == nameof(ISaga<TestSagaState, TestEvent>.ReactAsync)) == 4,
            "the saga is retried until it succeeds");
        await service.StopAsync(Token);

        Fake.GetCalls(_saga).Count(call => call.Method.Name == nameof(ISaga<TestSagaState, TestEvent>.ReactAsync)).Should().Be(4);
        A.CallTo(() => _parkedMessageSink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
