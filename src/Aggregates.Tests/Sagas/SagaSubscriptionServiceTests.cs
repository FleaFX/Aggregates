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
    readonly ISaga<TestSagaState, TestEvent> _saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
    readonly ISagaIdResolver<TestEvent> _resolver = A.Fake<ISagaIdResolver<TestEvent>>();
    readonly FakeSagaTimeoutStore _timeouts = new() { Deserializer = (type, _) => type == "Test.Command@v1" ? new TestCommand() : null };

    // Park on the first failure, so the tests don't wait for retries.
    SubscriptionErrorHandlingOptions _errorOptions = new() { MaxRetries = 1 };

    public SagaSubscriptionServiceTests() {
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId]);
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
    }

    static CancellationToken Token => TestContext.Current.CancellationToken;

    // The interface itself as TSaga, so the saga can be faked behind the real handler chain.
    SagaSubscriptionService<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildService(FakeSubscriptionFactory factory) {
        var registration = new SubscriptionRegistration(SubscriptionId, typeof(TestSaga), typeof(TestEvent), StartFromEnd: false);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_saga)
            .AddSingleton(_repository)
            .AddSingleton(_commandDispatcher)
            .AddSingleton<SagaCommitDelegate>(_ => ValueTask.CompletedTask)
            .AddSingleton<ISagaTimeoutStore>(_timeouts)
            .AddSingleton(typeof(SagaTimeoutActivation<>))
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
            new SagaTimeoutScheduler(_timeouts, _store, factory, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SagaTimeoutScheduler>.Instance),
            new SagaTimeoutDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), retryPolicy, _parkedMessageSink,
                NullLogger<SagaTimeoutDispatcher>.Instance),
            new SagaTimeoutActivation<ISaga<TestSagaState, TestEvent>>(_timeouts, [registration]),
            registration,
            _resolver);
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
    public async Task GivenResolverFails_ParksEvent_AndContinues() {
        var failure = new InvalidOperationException("resolver failed");
        A.CallTo(() => _resolver.Resolve(new TestEvent(3), A<EventMetadata>._)).Throws(failure);
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
    public async Task GivenSecondSagaFails_DoesNotRunFirstSagaAgain() {
        _errorOptions = new SubscriptionErrorHandlingOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var secondSagaId = new AggregateIdentifier("saga-2");
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId, secondSagaId]);
        HandledFor(secondSagaId).Throws(new InvalidOperationException("saga failed"));
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new TestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        HandledFor(secondSagaId).MustHaveHappened(3, Times.Exactly);
        HandledFor(SagaId).MustHaveHappenedOnceExactly();
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ExecutesAnOpenTimeout_ByDispatchingItsCommand() {
        _timeouts.Append("timeouts-sub-1", SagaTimeoutFormat.MarkerEventType, "activated"u8.ToArray());
        _timeouts.AppendRequest("saga-1", SubscriptionId, DateTimeOffset.UtcNow.AddDays(-1));
        var dispatched = false;
        A.CallTo(() => _commandDispatcher.DispatchAsync(A<ICommand>.That.IsInstanceOf(typeof(TestCommand)), A<CancellationToken>._))
            .Invokes(_ => dispatched = true);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new OtherTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await Eventually.UntilAsync(() => dispatched, "the planned command is dispatched");
        await service.StopAsync(Token);

        A.CallTo(() => _commandDispatcher.DispatchAsync(A<ICommand>.That.IsInstanceOf(typeof(TestCommand)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
