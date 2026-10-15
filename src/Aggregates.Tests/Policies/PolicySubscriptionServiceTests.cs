using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aggregates.Policies;

public class PolicySubscriptionServiceTests {
    const string SubscriptionId = "sub-1";

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly IPolicy<PolicyTestEvent> _handler = A.Fake<IPolicy<PolicyTestEvent>>();
    readonly ICommandDispatcher _commandDispatcher = A.Fake<ICommandDispatcher>();
    readonly FakeScheduledStore _scheduled = new() { Deserializer = (type, _) => type == "Test.Command@v1" ? new PolicyTestCommand() : null };

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public PolicySubscriptionServiceTests() =>
        A.CallTo(() => _handler.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

    // The interface itself as TPolicy, so the policy can be faked behind the real handler chain.
    PolicySubscriptionService<IPolicy<PolicyTestEvent>, PolicyTestEvent> BuildService(FakeSubscriptionFactory factory) {
        var parkedMessageSink = A.Fake<IParkedMessageSink>();
        var registration = new SubscriptionRegistration(SubscriptionId, typeof(IPolicy<PolicyTestEvent>), typeof(PolicyTestEvent), StartFromEnd: false);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_handler)
            .AddSingleton(_commandDispatcher)
            .AddSingleton<IScheduledStore>(_scheduled)
            .AddSingleton(registration)
            .AddSingleton(typeof(SchedulerActivation<>))
            .AddScoped(typeof(LoggingPolicyHandler<,>))
            .AddScoped(typeof(PolicyHandler<,>));
        var provider = services.BuildServiceProvider();
        var retryPolicy = new SubscriptionRetryPolicy(parkedMessageSink, new SubscriptionErrorHandlingOptions());
        return new(
            new SubscriptionLoop(factory, _store, parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            provider.GetRequiredService<IServiceScopeFactory>(),
            retryPolicy,
            new Scheduler(_scheduled, _store, factory, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), provider.GetRequiredService<IServiceScopeFactory>(), retryPolicy,
                parkedMessageSink, TimeProvider.System, NullLoggerFactory.Instance),
            provider.GetRequiredService<SchedulerActivation<IPolicy<PolicyTestEvent>>>(),
            registration);
    }

    IEnumerable<ulong> StoredPositions() =>
        Fake.GetCalls(_store)
            .Where(call => call.Method.Name == nameof(ICheckpointStore.StoreAsync))
            .Select(call => call.GetArgument<ulong>(1));

    [Fact]
    public async Task GivenOneMatchingEventAmongOthers_HandlesItOnce_AndStoresCheckpointsInBatches() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(251,
            position => position == 1 ? new PolicyTestEvent(1) : new OtherPolicyTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        A.CallTo(() => _handler.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _handler.ReactAsync(new PolicyTestEvent(1), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        StoredPositions().Should().Equal(100UL, 200UL, 251UL);
    }

    [Fact]
    public async Task GivenStopped_StoresLastProcessedPosition() {
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(3,
            position => new OtherPolicyTestEvent((int)position)));
        using var service = BuildService(factory);
        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);

        await service.StopAsync(Token);

        StoredPositions().Should().Equal(3UL);
    }

    [Fact]
    public async Task StoresADeferredCommand_InTheRequestStreamOfItsEvent() {
        A.CallTo(() => _handler.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new PolicyTestCommand().DeferUntil(DateTimeOffset.UtcNow.AddDays(1)) }.ToAsyncEnumerable());
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new PolicyTestEvent(1)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await service.StopAsync(Token);

        _scheduled.Entries.Should().ContainSingle(entry =>
            entry.Source.StreamId == "scheduled-requests-sub-1-1@stream-1" && entry.EventType == ScheduledFormat.CommandRequestEventType);
    }

    [Fact]
    public async Task ExecutesAnOpenDeferredCommand_ByDispatchingItsCommand() {
        _scheduled.Append("scheduled-sub-1", ScheduledFormat.MarkerEventType, "activated"u8.ToArray());
        _scheduled.AppendRequest("scheduled-requests-sub-1-0@stream-0", SubscriptionId, DateTimeOffset.UtcNow.AddDays(-1));
        var dispatched = false;
        A.CallTo(() => _commandDispatcher.DispatchAsync(A<ICommand>.That.IsInstanceOf(typeof(PolicyTestCommand)), A<CancellationToken>._))
            .Invokes(_ => dispatched = true);
        var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, position => new OtherPolicyTestEvent((int)position)));
        using var service = BuildService(factory);

        await service.StartAsync(Token);
        await factory.Drained.WaitAsync(Token);
        await Eventually.UntilAsync(() => dispatched, "the deferred command is dispatched");
        await service.StopAsync(Token);

        A.CallTo(() => _commandDispatcher.DispatchAsync(A<ICommand>.That.IsInstanceOf(typeof(PolicyTestCommand)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
