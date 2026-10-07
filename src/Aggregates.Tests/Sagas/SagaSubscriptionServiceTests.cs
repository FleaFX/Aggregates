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
    readonly ISagaHandler<TestSagaState, TestEvent> _handler = A.Fake<ISagaHandler<TestSagaState, TestEvent>>();
    readonly ISagaIdResolver<TestEvent> _resolver = A.Fake<ISagaIdResolver<TestEvent>>();

    // Park on the first failure, so the tests don't wait for retries.
    SubscriptionErrorHandlingOptions _errorOptions = new() { MaxRetries = 1 };

    public SagaSubscriptionServiceTests() =>
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId]);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    SagaSubscriptionService<TestSagaState, TestEvent> BuildService(FakeSubscriptionFactory factory) =>
        new(new SubscriptionLoop(factory, _store, _parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            _resolver,
            new ServiceCollection().AddSingleton(_handler).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new SubscriptionRetryPolicy(_parkedMessageSink, _errorOptions),
            SubscriptionId,
            startFromEnd: false);

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

        A.CallTo(() => _handler.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _handler.HandleAsync(SagaId, new TestEvent(1), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
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
        A.CallTo(() => _handler.HandleAsync(SagaId, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappened(4, Times.Exactly);
        A.CallTo(() => _handler.HandleAsync(SagaId, new TestEvent(3), A<CancellationToken>._)).MustNotHaveHappened();
        StoredPositions().Should().Equal(5UL);
    }

    [Fact]
    public async Task GivenSecondSagaFails_DoesNotRunFirstSagaAgain() {
        _errorOptions = new SubscriptionErrorHandlingOptions { MaxRetries = 3, InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var secondSagaId = new AggregateIdentifier("saga-2");
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId, secondSagaId]);
        A.CallTo(() => _handler.HandleAsync(secondSagaId, A<TestEvent>._, A<CancellationToken>._)).Throws(new InvalidOperationException("saga failed"));
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new TestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        A.CallTo(() => _handler.HandleAsync(secondSagaId, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => _handler.HandleAsync(SagaId, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).MustHaveHappenedOnceExactly();
    }
}
