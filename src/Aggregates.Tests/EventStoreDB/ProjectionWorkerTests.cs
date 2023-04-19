using EventStore.Client;
using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.EventStoreDB; 

public class ProjectionWorkerTests {
    readonly IServiceScopeFactory _serviceScopeFactory;

    readonly ListToAllAsyncDelegate _listToAllAsync;
    readonly CreateToAllAsyncDelegate _createToAllAsync;
    readonly SubscribeToAllAsync _subscribeToAllAsync;

    readonly ProjectionWorker<ExampleProjection, IExampleProjectionEvent> _worker;

    public ProjectionWorkerTests() {
        _listToAllAsync = A.Fake<ListToAllAsyncDelegate>();
        _createToAllAsync = A.Fake<CreateToAllAsyncDelegate>();
        _subscribeToAllAsync = A.Fake<SubscribeToAllAsync>();

        var serviceProvider = A.Dummy<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ListToAllAsyncDelegate))).Returns(_listToAllAsync);
        A.CallTo(() => serviceProvider.GetService(typeof(CreateToAllAsyncDelegate))).Returns(_createToAllAsync);
        A.CallTo(() => serviceProvider.GetService(typeof(SubscribeToAllAsync))).Returns(_subscribeToAllAsync);
        A.CallTo(() => serviceProvider.GetService(typeof(ResolvedEventDeserializer))).Returns(new ResolvedEventDeserializer((source, target) => null!));

        var serviceScope = A.Dummy<IServiceScope>();
        A.CallTo(() => serviceScope.ServiceProvider).Returns(serviceProvider);

        _serviceScopeFactory = A.Dummy<IServiceScopeFactory>();
        A.CallTo(() => _serviceScopeFactory.CreateScope()).Returns(serviceScope);

        _worker = new ProjectionWorker<ExampleProjection, IExampleProjectionEvent>(_serviceScopeFactory, new Logger<ProjectionWorker<ExampleProjection, IExampleProjectionEvent>>(A.Dummy<ILoggerFactory>()));
    }


    [Fact]
    public async Task WhenSubscriptionExists_ThenDoesNotCreateNewSubscription() {
        var groupName = typeof(ExampleProjection).FullName!;

        A.CallTo(() => _listToAllAsync(A<TimeSpan?>._, A<UserCredentials?>._, A<CancellationToken>._))
            .Returns(new[] {
                new PersistentSubscriptionInfo(
                    null, groupName,
                    null, Enumerable.Empty<PersistentSubscriptionConnectionInfo>(), null, null)
            });
        
        var cts = new CancellationTokenSource();
        await _worker.StartAsync(cts.Token);
        cts.Cancel();

        A.CallTo(_createToAllAsync).MustNotHaveHappened();
    }

    [Fact]
    public async Task WhenSubscriptionDoesNotExist_ThenCreatesNewSubscription() {
        var groupName = typeof(ExampleProjection).FullName;

        A.CallTo(() => _listToAllAsync(A<TimeSpan?>._, A<UserCredentials?>._, A<CancellationToken>._))
            .Returns(Enumerable.Empty<PersistentSubscriptionInfo>());

        var cts = new CancellationTokenSource();
        await _worker.StartAsync(cts.Token);
        cts.Cancel();

        A.CallTo(() => _createToAllAsync(
                groupName,
                EventTypeFilter.RegularExpression(@"^(?:Projections\.Tests\.EventType1@v1|Projections\.Tests\.EventType2@v1)$", 32U),
                A<PersistentSubscriptionSettings>._, A<TimeSpan?>._, A<UserCredentials?>._, A<CancellationToken>._))
            .MustHaveHappened();
    }


    interface IExampleProjectionEvent { }

    [EventContract(nameof(EventType1), @namespace: "Projections.Tests")]
    record struct EventType1(string StringValue) : IExampleProjectionEvent;
    [EventContract(nameof(EventType2), @namespace: "Projections.Tests")]
    record struct EventType2(int NumberValue) : IExampleProjectionEvent;
    record ExampleProjection : IProjection<ExampleProjection, IExampleProjectionEvent> {
        public static ExampleProjection Initial => new();

        public ExampleProjection Apply(IExampleProjectionEvent @event) =>
            @event switch {
                EventType1 eventType1 => this,
                EventType2 eventType2 => this,
                _ => throw new ArgumentOutOfRangeException(nameof(@event))
            };
    }
}