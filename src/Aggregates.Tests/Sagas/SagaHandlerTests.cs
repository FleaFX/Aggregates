using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaHandlerTests {
    // The interface itself as TSaga, so the saga can be faked.
    static SagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildHandler(
        ISagaRepository<TestSagaState, TestEvent> repository,
        ISaga<TestSagaState, TestEvent> saga,
        ICommandDispatcher dispatcher,
        ISagaTimeoutStore? store = null) =>
        new(repository, saga, dispatcher, new SagaTimeoutActivation<ISaga<TestSagaState, TestEvent>>(
            store ?? A.Fake<ISagaTimeoutStore>(),
            [new SubscriptionRegistration("Test.Saga@v1", typeof(ISaga<TestSagaState, TestEvent>), typeof(TestEvent), false)]));

    [Fact]
    public async Task HandleAsync_WhenSagaNotFound_CreatesSagaRoot() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var source = new EventReference("stream-1", 0);

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), source, TestContext.Current.CancellationToken);

        A.CallTo(() => repository.Add(A<AggregateIdentifier>._, A<SagaRoot<TestSagaState, TestEvent>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_PassesTheSourceToTheSagaRoot() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        SagaRoot<TestSagaState, TestEvent>? createdRoot = null;
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => repository.Add(A<AggregateIdentifier>._, A<SagaRoot<TestSagaState, TestEvent>>._))
            .Invokes(call => createdRoot = call.GetArgument<SagaRoot<TestSagaState, TestEvent>>(1));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 7), TestContext.Current.CancellationToken);

        createdRoot.Should().NotBeNull();
        createdRoot!.GetChanges().Should().Contain(new EventReference("stream-1", 7));
    }

    [Fact]
    public async Task HandleAsync_WhenSagaExists_UsesExistingRoot() {
        var existingRoot = new SagaRoot<TestSagaState, TestEvent>(default, new AggregateVersion(0));
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(existingRoot));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(() => repository.Add(A<AggregateIdentifier>._, A<SagaRoot<TestSagaState, TestEvent>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task HandleAsync_DispatchesProducedCommands() {
        var command = new TestCommand();
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WhenNoCommandsProduced_DoesNotCallDispatcher() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task HandleAsync_WithARequestedTimeout_ActivatesTheSchedulerBeforeDispatching() {
        var command = new TestCommand();
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        var store = A.Fake<ISagaTimeoutStore>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { SagaTimeout.At(DateTimeOffset.UtcNow, command), command }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher, store).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(() => store.AppendAsync("timeouts-Test.Saga@v1", SagaTimeoutFormat.MarkerEventType, A<ReadOnlyMemory<byte>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task HandleAsync_WithoutTimeouts_DoesNotTouchTheTimeoutStore() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        var store = A.Fake<ISagaTimeoutStore>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new TestCommand() }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher, store).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(store).MustNotHaveHappened();
    }
}
