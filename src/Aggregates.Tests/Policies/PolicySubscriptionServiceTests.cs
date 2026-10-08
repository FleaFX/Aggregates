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

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public PolicySubscriptionServiceTests() =>
        A.CallTo(() => _handler.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

    // The interface itself as TPolicy, so the policy can be faked behind the real handler chain.
    PolicySubscriptionService<IPolicy<PolicyTestEvent>, PolicyTestEvent> BuildService(FakeSubscriptionFactory factory) {
        var parkedMessageSink = A.Fake<IParkedMessageSink>();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_handler)
            .AddSingleton(A.Fake<ICommandDispatcher>())
            .AddScoped(typeof(LoggingPolicyHandler<,>))
            .AddScoped(typeof(PolicyHandler<,>));
        return new(
            new SubscriptionLoop(factory, _store, parkedMessageSink, new SubscriptionCheckpointOptions(),
                new SubscriptionResubscribeOptions(), TimeProvider.System, NullLogger<SubscriptionLoop>.Instance),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new SubscriptionRetryPolicy(parkedMessageSink, new SubscriptionErrorHandlingOptions()),
            new SubscriptionRegistration(SubscriptionId, typeof(TestPolicy), typeof(PolicyTestEvent), StartFromEnd: false));
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
}
