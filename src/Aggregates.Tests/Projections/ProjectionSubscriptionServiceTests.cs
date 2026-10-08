using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aggregates.Projections;

public class ProjectionSubscriptionServiceTests {
    const string SubscriptionId = "sub-1";

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly IProjection<ProjectionTestEvent> _handler = A.Fake<IProjection<ProjectionTestEvent>>();

    static CancellationToken Token => TestContext.Current.CancellationToken;

    // The interface itself as TProjection, so the projection can be faked behind the real handler chain.
    ProjectionSubscriptionService<IProjection<ProjectionTestEvent>, ProjectionTestEvent> BuildService(FakeSubscriptionFactory factory) {
        var parkedMessageSink = A.Fake<IParkedMessageSink>();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_handler)
            .AddScoped(typeof(LoggingProjectionHandler<,>))
            .AddScoped(typeof(ProjectionHandler<,>));
        return new(
            new SubscriptionLoop(factory, _store, parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new SubscriptionRetryPolicy(parkedMessageSink, new SubscriptionErrorHandlingOptions()),
            new SubscriptionRegistration(SubscriptionId, typeof(TestProjection), typeof(ProjectionTestEvent), StartFromEnd: false));
    }

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
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        A.CallTo(() => _handler.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _handler.ProjectAsync(new ProjectionTestEvent(1), A<EventMetadata>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        StoredPositions().Should().Equal(100UL, 200UL, 251UL);
    }

    [Fact]
    public async Task GivenStopped_StoresLastProcessedPosition() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(3,
            position => new OtherProjectionTestEvent((int)position)));
        using var service = BuildService(factory);
        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);

        await service.StopAsync(Token);

        StoredPositions().Should().Equal(3UL);
    }
}
