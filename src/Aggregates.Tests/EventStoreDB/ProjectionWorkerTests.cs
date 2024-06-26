﻿using Aggregates.Configuration;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.EventStoreDB.Workers;
using Aggregates.Sql;
using EventStore.Client;
using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.EventStoreDB;

public class ProjectionWorkerTests {
    readonly IServiceScopeFactory _serviceScopeFactory;

    readonly ListToAllAsyncDelegate _listToAllAsync;
    readonly CreateToAllAsyncDelegate _createToAllAsync;
    readonly SubscribeToAll _subscribeToAll;

    readonly ProjectionWorker<ExampleProjection, IExampleProjectionEvent> _worker;

    public ProjectionWorkerTests() {
        _listToAllAsync = A.Fake<ListToAllAsyncDelegate>();
        _createToAllAsync = A.Fake<CreateToAllAsyncDelegate>();
        _subscribeToAll = A.Fake<SubscribeToAll>();

        var serviceProvider = A.Dummy<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ListToAllAsyncDelegate))).Returns(_listToAllAsync);
        A.CallTo(() => serviceProvider.GetService(typeof(CreateToAllAsyncDelegate))).Returns(_createToAllAsync);
        A.CallTo(() => serviceProvider.GetService(typeof(SubscribeToAll))).Returns(_subscribeToAll);
        A.CallTo(() => serviceProvider.GetService(typeof(ResolvedEventDeserializer))).Returns(new ResolvedEventDeserializer((source, target) => null!, new AggregatesOptions()));
        A.CallTo(() => serviceProvider.GetService(typeof(MetadataDeserializer))).Returns(new MetadataDeserializer((source, target) => null!));
        A.CallTo(() => serviceProvider.GetService(typeof(AggregatesOptions))).Returns(new AggregatesOptions());
        A.CallTo(() => serviceProvider.GetService(typeof(IProjection<ExampleProjection, IExampleProjectionEvent>))).Returns(new ExampleProjection(A.Dummy<IDbConnectionFactory>()));

        var serviceScope = A.Dummy<IServiceScope>();
        A.CallTo(() => serviceScope.ServiceProvider).Returns(serviceProvider);

        _serviceScopeFactory = A.Dummy<IServiceScopeFactory>();
        A.CallTo(() => _serviceScopeFactory.CreateScope()).Returns(serviceScope);

        _worker = new ProjectionWorker<ExampleProjection, IExampleProjectionEvent>(_serviceScopeFactory, null!);
    }


    [Fact]
    public async Task WhenSubscriptionExists_ThenDoesNotCreateNewSubscription() {
        var groupName = typeof(ExampleProjection).FullName!;

        A.CallTo(() => _listToAllAsync(A<TimeSpan?>._, A<UserCredentials?>._, A<CancellationToken>._))
            .Returns(new[] {
                new PersistentSubscriptionInfo(
                    null, groupName,
                    null, [], null, null)
            });

        var cts = new CancellationTokenSource();
        await _worker.StartAsync(cts.Token);
        await cts.CancelAsync();

        A.CallTo(_createToAllAsync).MustNotHaveHappened();
    }

    [Fact]
    public async Task WhenSubscriptionDoesNotExist_ThenCreatesNewSubscription() {
        var groupName = typeof(ExampleProjection).FullName;

        A.CallTo(() => _listToAllAsync(A<TimeSpan?>._, A<UserCredentials?>._, A<CancellationToken>._))
            .Returns([]);

        var cts = new CancellationTokenSource();
        await _worker.StartAsync(cts.Token);
        await cts.CancelAsync();

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
    record ExampleProjection(IDbConnectionFactory DbConnectionFactory) : SqlProjection<ExampleProjection, IExampleProjectionEvent>(DbConnectionFactory) {
        public override ICommit<ExampleProjection> Apply(IExampleProjectionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null) =>
            @event switch {
                EventType1 eventType1 => Query("").Query(""),
                EventType2 eventType2 => Query("").Query(""),
                _ => throw new ArgumentOutOfRangeException(nameof(@event))
            };
    }
}