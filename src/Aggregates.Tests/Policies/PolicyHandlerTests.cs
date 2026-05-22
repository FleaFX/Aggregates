using FakeItEasy;

namespace Aggregates.Policies;

public class PolicyHandlerTests {
    static PolicyHandler<TestPolicy, PolicyTestEvent> BuildHandler(
        IPolicy<PolicyTestEvent> policy,
        ICommandDispatcher dispatcher) =>
        new(policy, dispatcher);

    [Fact]
    public async Task HandleAsync_CallsReactAsyncWithTheEvent() {
        var policy = A.Fake<IPolicy<PolicyTestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var @event = new PolicyTestEvent(42);

        await BuildHandler(policy, dispatcher).HandleAsync(@event, TestContext.Current.CancellationToken);

        A.CallTo(() => policy.ReactAsync(@event, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_DispatchesProducedCommands() {
        ICommand command = new PolicyTestCommand();
        var policy = A.Fake<IPolicy<PolicyTestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(new[] { command }.ToAsyncEnumerable());

        await BuildHandler(policy, dispatcher).HandleAsync(new PolicyTestEvent(1), TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WhenNoCommandsProduced_DoesNotCallDispatcher() {
        var policy = A.Fake<IPolicy<PolicyTestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        await BuildHandler(policy, dispatcher).HandleAsync(new PolicyTestEvent(1), TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
