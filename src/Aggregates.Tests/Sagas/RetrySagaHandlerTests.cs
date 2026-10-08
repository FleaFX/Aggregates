using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class RetrySagaHandlerTests {
    // The repository is the first thing the innermost handler calls, so it stands in for the inner handler.
    readonly ISagaRepository<TestSagaState, TestEvent> _repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
    readonly ISaga<TestSagaState, TestEvent> _saga = A.Fake<ISaga<TestSagaState, TestEvent>>();

    public RetrySagaHandlerTests() {
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
    }

    [Fact]
    public async Task GivenSuccessOnFirstAttempt_InvokesInnerOnce() {
        await BuildHandler(maxAttempts: 3).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        InnerCalls().MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GivenConcurrencyExceptionThenSuccess_Retries() {
        InnerCalls()
            .Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None)).Twice()
            .Then.Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));

        await BuildHandler(maxAttempts: 3).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        InnerCalls().MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task GivenConcurrencyExceptionExceedsMaxAttempts_Throws() {
        InnerCalls().Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None));

        var act = () => BuildHandler(maxAttempts: 3).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task GivenNonConcurrencyException_DoesNotRetry() {
        InnerCalls().Throws<InvalidOperationException>();

        var act = () => BuildHandler(maxAttempts: 3).HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        InnerCalls().MustHaveHappenedOnceExactly();
    }

    FakeItEasy.Configuration.IReturnValueArgumentValidationConfiguration<ValueTask<SagaRoot<TestSagaState, TestEvent>?>> InnerCalls() =>
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._));

    // The interface itself as TSaga, so the saga can be faked.
    RetrySagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildHandler(int maxAttempts) =>
        new(new UnitOfWorkAwareSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(
            new SagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(_repository, _saga, A.Fake<ICommandDispatcher>()),
            _ => ValueTask.CompletedTask), maxAttempts);
}
