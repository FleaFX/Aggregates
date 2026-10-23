using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class UnitOfWorkAwareSagaHandlerTests {
    readonly ISagaRepository<TestSagaState, TestEvent> _repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
    readonly ISaga<TestSagaState, TestEvent, TestDeadline> _saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
    CancellationToken? _committedWith;

    public UnitOfWorkAwareSagaHandlerTests() {
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
    }

    [Fact]
    public async Task HandleAsync_PassesItsCancellationTokenToTheCommitDelegate() {
        using var cts = new CancellationTokenSource();
        var handler = BuildHandler();

        await handler.HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), cts.Token);

        _committedWith.Should().Be(cts.Token);
    }

    [Fact]
    public async Task HandleDeadlineAsync_PassesItsCancellationTokenToTheCommitDelegate() {
        using var cts = new CancellationTokenSource();
        var handler = BuildHandler();

        var found = await handler.HandleDeadlineAsync("saga-1", new TestDeadline(1), new EventReference("saga-1", 2), cts.Token);

        found.Should().BeFalse();
        _committedWith.Should().Be(cts.Token);
    }

    // The interface itself as TSaga, so the saga can be faked.
    UnitOfWorkAwareSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildHandler() =>
        new(new SagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(_repository, _saga, A.Fake<ICommandDispatcher>(), new SchedulerActivation<ISaga<TestSagaState, TestEvent>>(A.Fake<IScheduledStore>(), [])),
            (_, cancellationToken) => { _committedWith = cancellationToken; return ValueTask.CompletedTask; });
}
