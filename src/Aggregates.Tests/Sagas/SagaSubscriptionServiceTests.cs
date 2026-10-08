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

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly IParkedMessageSink _parkedMessageSink = A.Fake<IParkedMessageSink>();
    readonly ISagaRepository<TestSagaState, TestEvent> _repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
    readonly ISaga<TestSagaState, TestEvent> _saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
    readonly ISagaIdResolver<TestEvent> _resolver = A.Fake<ISagaIdResolver<TestEvent>>();

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
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_saga)
            .AddSingleton(_repository)
            .AddSingleton(A.Fake<ICommandDispatcher>())
            .AddSingleton<SagaCommitDelegate>(_ => ValueTask.CompletedTask)
            .AddScoped(typeof(LoggingSagaHandler<,,>))
            .AddScoped(typeof(RetrySagaHandler<,,>))
            .AddScoped(typeof(UnitOfWorkAwareSagaHandler<,,>))
            .AddScoped(typeof(SagaHandler<,,>));
        return new(new SubscriptionLoop(factory, _store, _parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new SubscriptionRetryPolicy(_parkedMessageSink, _errorOptions),
            new SubscriptionRegistration(SubscriptionId, typeof(TestSaga), typeof(TestEvent), StartFromEnd: false),
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
}
