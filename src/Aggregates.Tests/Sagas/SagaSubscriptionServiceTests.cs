using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas;

public class SagaSubscriptionServiceTests {
    const string SubscriptionId = "sub-1";
    static readonly AggregateIdentifier SagaId = new("saga-1");

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly ISagaHandler<TestSagaState, TestEvent> _handler = A.Fake<ISagaHandler<TestSagaState, TestEvent>>();
    readonly ISagaIdResolver<TestEvent> _resolver = A.Fake<ISagaIdResolver<TestEvent>>();

    public SagaSubscriptionServiceTests() =>
        A.CallTo(() => _resolver.Resolve(A<TestEvent>._, A<EventMetadata>._)).Returns([SagaId]);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    SagaSubscriptionService<TestSagaState, TestEvent> BuildService(FakeSubscriptionFactory factory) =>
        new(factory,
            _resolver,
            new ServiceCollection().AddSingleton(_handler).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _store,
            new SubscriptionRetryPolicy(A.Fake<IParkedMessageSink>(), new SubscriptionErrorHandlingOptions()),
            new SubscriptionCheckpointOptions(),
            TimeProvider.System,
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
        await service.ExecuteTask!.WaitAsync(Token);

        A.CallTo(() => _handler.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _handler.HandleAsync(SagaId, new TestEvent(1), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        StoredPositions().Should().Equal(100UL, 200UL, 251UL);
    }

    [Fact]
    public async Task GivenStopped_StoresLastProcessedPosition() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(3,
            position => new OtherTestEvent((int)position)), waitAtEnd: true);
        using var service = BuildService(factory);
        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);

        await service.StopAsync(Token);

        StoredPositions().Should().Equal(3UL);
    }

    [Fact]
    public async Task GivenFailureOutsideRetryPolicy_StoresOnlyPositionsBeforeIt() {
        A.CallTo(() => _resolver.Resolve(new TestEvent(3), A<EventMetadata>._)).Throws(new InvalidOperationException("resolver failed"));
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(5,
            position => new TestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        var act = () => service.ExecuteTask!.WaitAsync(Token);

        await act.Should().ThrowAsync<InvalidOperationException>();
        StoredPositions().Should().Equal(2UL);
    }
}
