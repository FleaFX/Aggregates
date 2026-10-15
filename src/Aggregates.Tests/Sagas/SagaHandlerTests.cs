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
        IScheduledStore? store = null) =>
        new(repository, saga, dispatcher, new SchedulerActivation<ISaga<TestSagaState, TestEvent>>(
            store ?? A.Fake<IScheduledStore>(),
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
    public async Task HandleAsync_WithARequestedDeferredCommand_ActivatesTheSchedulerBeforeDispatching() {
        var command = new TestCommand();
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { command.DeferUntil(DateTimeOffset.UtcNow), command }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher, store).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(() => store.AppendAsync("scheduled-Test.Saga@v1", ScheduledFormat.MarkerEventType, A<ReadOnlyMemory<byte>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task HandleAsync_WithARequestedDeadline_ActivatesTheSchedulerBeforeDispatching() {
        var command = new TestCommand();
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { Deadline.At(DateTimeOffset.UtcNow, new TestDeadline(1)), command }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher, store).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(() => store.AppendAsync("scheduled-Test.Saga@v1", ScheduledFormat.MarkerEventType, A<ReadOnlyMemory<byte>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task HandleAsync_WithoutDeferredCommands_DoesNotTouchTheScheduledStore() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new TestCommand() }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher, store).HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        A.CallTo(store).MustNotHaveHappened();
    }

    [Fact]
    public async Task HandleDeadlineAsync_ReactsWithTheLoadedState_AndDispatchesItsCommands() {
        var command = new TestCommand();
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        TestSagaState? capturedState = null;
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(4), new AggregateVersion(1))));
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Invokes(call => capturedState = call.GetArgument<TestSagaState>(0))
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());

        var found = await BuildHandler(repository, saga, dispatcher).HandleDeadlineAsync("saga-1", new TestDeadline(1), new EventReference("saga-1", 2), TestContext.Current.CancellationToken);

        capturedState.Should().Be(new TestSagaState(4));
        A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        found.Should().BeTrue();
    }

    [Fact]
    public async Task HandleDeadlineAsync_WhenTheSagaDoesNotExist_ReturnsFalse_WithoutCreatingIt() {
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));

        var found = await BuildHandler(repository, saga, dispatcher).HandleDeadlineAsync("saga-1", new TestDeadline(1), new EventReference("saga-1", 2), TestContext.Current.CancellationToken);

        found.Should().BeFalse();
        A.CallTo(() => repository.Add(A<AggregateIdentifier>._, A<SagaRoot<TestSagaState, TestEvent>>._)).MustNotHaveHappened();
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task HandleDeadlineAsync_WithARequestedDeadline_ActivatesTheScheduler() {
        var command = new TestCommand();
        var repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        var dispatcher = A.Fake<ICommandDispatcher>();
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(4), new AggregateVersion(1))));
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(new ICommand[] { Deadline.At(DateTimeOffset.UtcNow, new TestDeadline(1)), command }.ToAsyncEnumerable());

        await BuildHandler(repository, saga, dispatcher, store).HandleDeadlineAsync("saga-1", new TestDeadline(1), new EventReference("saga-1", 2), TestContext.Current.CancellationToken);

        A.CallTo(() => store.AppendAsync("scheduled-Test.Saga@v1", ScheduledFormat.MarkerEventType, A<ReadOnlyMemory<byte>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly());
    }
}
