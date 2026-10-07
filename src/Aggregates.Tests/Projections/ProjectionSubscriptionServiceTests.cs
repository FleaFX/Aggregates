using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections;

public class ProjectionSubscriptionServiceTests {
    const string SubscriptionId = "sub-1";

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly IProjectionHandler<ProjectionTestEvent> _handler = A.Fake<IProjectionHandler<ProjectionTestEvent>>();

    static CancellationToken Token => TestContext.Current.CancellationToken;

    ProjectionSubscriptionService<ProjectionTestEvent> BuildService(FakeSubscriptionFactory factory) =>
        new(factory,
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
            position => position == 1 ? new ProjectionTestEvent(1) : new OtherProjectionTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await service.ExecuteTask!.WaitAsync(Token);

        A.CallTo(() => _handler.HandleAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _handler.HandleAsync(new ProjectionTestEvent(1), A<EventMetadata>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        StoredPositions().Should().Equal(100UL, 200UL, 251UL);
    }

    [Fact]
    public async Task GivenStopped_StoresLastProcessedPosition() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(3,
            position => new OtherProjectionTestEvent((int)position)), waitAtEnd: true);
        using var service = BuildService(factory);
        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);

        await service.StopAsync(Token);

        StoredPositions().Should().Equal(3UL);
    }
}
