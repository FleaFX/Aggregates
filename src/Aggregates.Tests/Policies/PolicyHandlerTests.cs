using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Policies;

public class PolicyHandlerTests {
    const string Owner = "Tests.Policy@v1";
    static readonly EventReference Source = new("src-1", 3);
    static readonly DateTimeOffset Due = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    readonly IScheduledStore _store = A.Fake<IScheduledStore>();
    readonly FakeLogger<PolicyHandler<IPolicy<PolicyTestEvent>, PolicyTestEvent>> _logger = new();

    public PolicyHandlerTests() =>
        A.CallTo(() => _store.TryAppendRequestsAsync(A<string>._, A<IReadOnlyList<DeferredCommandRequest>>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult(true));

    // The interface itself as TPolicy, so the policy can be faked.
    PolicyHandler<IPolicy<PolicyTestEvent>, PolicyTestEvent> BuildHandler(
        IPolicy<PolicyTestEvent> policy,
        ICommandDispatcher dispatcher
    ) =>
        new(policy, dispatcher,
            new SchedulerActivation<IPolicy<PolicyTestEvent>>(_store,
                [new SubscriptionRegistration(Owner, typeof(IPolicy<PolicyTestEvent>), typeof(PolicyTestEvent), StartFromEnd: false)]),
            _store, _logger);

    static IPolicy<PolicyTestEvent> Reacting(params ICommand[] commands) {
        var policy = A.Fake<IPolicy<PolicyTestEvent>>();
        A.CallTo(() => policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._)).Returns(commands.ToAsyncEnumerable());
        return policy;
    }

    [Fact]
    public async Task HandleAsync_CallsReactAsyncWithTheEvent() {
        var policy = Reacting();
        var @event = new PolicyTestEvent(42);

        await BuildHandler(policy, A.Fake<ICommandDispatcher>()).HandleAsync(@event, Source, TestContext.Current.CancellationToken);

        A.CallTo(() => policy.ReactAsync(@event, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_DispatchesProducedCommands() {
        ICommand command = new PolicyTestCommand();
        var dispatcher = A.Fake<ICommandDispatcher>();

        await BuildHandler(Reacting(command), dispatcher).HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WhenNoCommandsProduced_DoesNotCallDispatcher() {
        var dispatcher = A.Fake<ICommandDispatcher>();

        await BuildHandler(Reacting(), dispatcher).HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task DeferredCommands_AreStoredAfterTheImmediateOnes_InOneAppend() {
        var (a, b, c, d) = (new NamedCommand("a"), new NamedCommand("b"), new NamedCommand("c"), new NamedCommand("d"));
        var dispatcher = A.Fake<ICommandDispatcher>();

        await BuildHandler(Reacting(a, b.DeferUntil(Due), c, d.DeferUntil(Due.AddDays(1))), dispatcher)
            .HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(a, A<CancellationToken>._)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => dispatcher.DispatchAsync(c, A<CancellationToken>._)).MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _store.TryAppendRequestsAsync("scheduled-requests-Tests.Policy@v1-3@src-1",
                A<IReadOnlyList<DeferredCommandRequest>>.That.IsSameSequenceAs(new DeferredCommandRequest[] {
                    new(Owner, Due, b), new(Owner, Due.AddDays(1), d) }),
                A<CancellationToken>._)).MustHaveHappenedOnceExactly());
        A.CallTo(() => dispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task DeferredCommands_ActivateTheSchedulerBeforeTheyAreStored() {
        await BuildHandler(Reacting(new NamedCommand("a").DeferUntil(Due)), A.Fake<ICommandDispatcher>())
            .HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        A.CallTo(() => _store.AppendAsync(ScheduledFormat.SchedulerStream(Owner), ScheduledFormat.MarkerEventType, A<ReadOnlyMemory<byte>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _store.TryAppendRequestsAsync(A<string>._, A<IReadOnlyList<DeferredCommandRequest>>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task FailingImmediateCommand_StoresNothing() {
        var failing = new NamedCommand("c");
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => dispatcher.DispatchAsync(failing, A<CancellationToken>._)).Throws<InvalidOperationException>();

        var act = () => BuildHandler(Reacting(new NamedCommand("b").DeferUntil(Due), failing), dispatcher)
            .HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        A.CallTo(() => _store.TryAppendRequestsAsync(A<string>._, A<IReadOnlyList<DeferredCommandRequest>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task AlreadyStored_IsNotAnError() {
        A.CallTo(() => _store.TryAppendRequestsAsync(A<string>._, A<IReadOnlyList<DeferredCommandRequest>>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult(false));

        await BuildHandler(Reacting(new NamedCommand("a").DeferUntil(Due)), A.Fake<ICommandDispatcher>())
            .HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Level == LogLevel.Debug && record.Message.Contains("already stored the deferred commands of 3@src-1"));
    }

    [Fact]
    public async Task WithoutDeferredCommands_DoesNotTouchTheStore() {
        await BuildHandler(Reacting(new NamedCommand("a")), A.Fake<ICommandDispatcher>())
            .HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        Fake.GetCalls(_store).Should().BeEmpty();
    }

    [Fact]
    public async Task TheOwnerIsTheSubscriptionIdOfThePolicy() {
        await BuildHandler(Reacting(new NamedCommand("a").DeferUntil(Due)), A.Fake<ICommandDispatcher>())
            .HandleAsync(new PolicyTestEvent(1), Source, TestContext.Current.CancellationToken);

        A.CallTo(() => _store.TryAppendRequestsAsync(A<string>._,
            A<IReadOnlyList<DeferredCommandRequest>>.That.Matches(requests => requests.All(request => request.Owner == Owner)),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    sealed record NamedCommand(string Name) : ICommand;
}
