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
    readonly IParkedMessageSink _parkedMessageSink = A.Fake<IParkedMessageSink>();

    static CancellationToken Token => TestContext.Current.CancellationToken;

    // The interface itself as TProjection, so the projection can be faked behind the real handler chain.
    ProjectionSubscriptionService<IProjection<ProjectionTestEvent>, ProjectionTestEvent> BuildService(FakeSubscriptionFactory factory) {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_handler)
            .AddScoped(typeof(LoggingProjectionHandler<,>))
            .AddScoped(typeof(ProjectionHandler<,>));
        return new(
            new SubscriptionLoop(factory, _store, _parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new SubscriptionRetryPolicy(_parkedMessageSink, new SubscriptionErrorHandlingOptions()),
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

    [Fact]
    public async Task HandleAsync_ProjectsTheEventOnce() {
        var metadata = new EventMetadata(new Dictionary<string, object?> { ["correlation"] = "c-1" });
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new ProjectionTestEvent(1)));
        using var service = BuildService(factory);

        await service.HandleAsync(new SubscriptionMessage(new ProjectionTestEvent(1), 42, new EventReference("stream-1", 1), metadata), Token);

        A.CallTo(() => _handler.ProjectAsync(new ProjectionTestEvent(1), metadata, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_DoesNotRetryOrPark_WhenTheProjectionFails() {
        var failure = new InvalidOperationException("projection failed");
        A.CallTo(() => _handler.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._)).Throws(failure);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new ProjectionTestEvent(1)));
        using var service = BuildService(factory);

        var act = () => service.HandleAsync(new SubscriptionMessage(new ProjectionTestEvent(1), 42, new EventReference("stream-1", 1), EventMetadata.Empty), Token).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        A.CallTo(() => _handler.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _parkedMessageSink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task HandleAsync_WithAnotherEventType_Throws() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new ProjectionTestEvent(1)));
        using var service = BuildService(factory);

        var act = () => service.HandleAsync(new SubscriptionMessage(new OtherProjectionTestEvent(1), 42, new EventReference("stream-1", 1), EventMetadata.Empty), Token).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*handles ProjectionTestEvent*");
    }

    [Fact]
    public void Registration_IsTheRegistrationOfTheService() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new ProjectionTestEvent(1)));
        using var service = BuildService(factory);

        service.Registration.SubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task HandleDeadlineAsync_Throws() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new ProjectionTestEvent(1)));
        using var service = BuildService(factory);

        var act = () => ((IReplayableSubscription)service).HandleDeadlineAsync(
            new ScheduledDeadline(new EventReference("saga-1", 1), DateTimeOffset.UtcNow, new object(), EventMetadata.Empty, 1), Token).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*receives no deadlines*");
    }
}
