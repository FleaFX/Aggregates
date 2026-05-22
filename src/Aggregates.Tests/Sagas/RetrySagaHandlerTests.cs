using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class RetrySagaHandlerTests {
    [Fact]
    public async Task GivenSuccessOnFirstAttempt_InvokesInnerOnce() {
        var inner = A.Fake<SagaHandler<TestSagaState, TestEvent>>();
        var handler = BuildHandler(inner, maxAttempts: 3);

        await handler.HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GivenConcurrencyExceptionThenSuccess_Retries() {
        var inner = A.Fake<SagaHandler<TestSagaState, TestEvent>>();
        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None)).Twice()
            .Then.Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner, maxAttempts: 3);

        await handler.HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task GivenConcurrencyExceptionExceedsMaxAttempts_Throws() {
        var inner = A.Fake<SagaHandler<TestSagaState, TestEvent>>();
        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None));
        var handler = BuildHandler(inner, maxAttempts: 3);

        var act = () => handler.HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task GivenNonConcurrencyException_DoesNotRetry() {
        var inner = A.Fake<SagaHandler<TestSagaState, TestEvent>>();
        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        var handler = BuildHandler(inner, maxAttempts: 3);

        var act = () => handler.HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    static RetrySagaHandler<TestSagaState, TestEvent> BuildHandler(SagaHandler<TestSagaState, TestEvent> inner, int maxAttempts = 3) =>
        new(new UnitOfWorkAwareSagaHandler<TestSagaState, TestEvent>(inner, _ => ValueTask.CompletedTask), maxAttempts);
}
