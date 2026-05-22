using FakeItEasy;

namespace Aggregates.Sagas;

public class SagaHandlerTests {
    static SagaHandler<TestSaga, TestSagaState, TestEvent> BuildHandler(
        ISagaRepository<TestSagaState, TestEvent> repository,
        ISaga<TestSagaState, TestEvent> saga,
        ICommandDispatcher dispatcher) =>
        new(repository, saga, dispatcher);

    [Fact]
    public async Task HandleAsync_WhenSagaNotFound_CreatesSagaRoot() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        A.CallTo(() => repository.Add(A<AggregateIdentifier>._, A<SagaRoot<TestSagaState, TestEvent>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WhenSagaExists_UsesExistingRoot() {
        var existingRoot = new SagaRoot<TestSagaState, TestEvent>(new AggregateVersion(0));
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(existingRoot));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

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

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

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

        await BuildHandler(repository, saga, dispatcher).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        A.CallTo(() => dispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
