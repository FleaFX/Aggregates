using FakeItEasy;
using FluentAssertions;

namespace Aggregates;

public class RetryCommandHandlerTests {
    [Fact]
    public async Task GivenSuccessOnFirstAttempt_InvokesInnerOnce() {
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        var handler = BuildHandler(inner, maxAttempts: 3);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GivenConcurrencyExceptionThenSuccess_Retries() {
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None)).Twice()
            .Then.Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner, maxAttempts: 3);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task GivenConcurrencyExceptionExceedsMaxAttempts_Throws() {
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None));
        var handler = BuildHandler(inner, maxAttempts: 3);

        var act = () => handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task GivenNonConcurrencyException_DoesNotRetry() {
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        var handler = BuildHandler(inner, maxAttempts: 3);

        var act = () => handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    static RetryCommandHandler<NoopCommand> BuildHandler(CommandHandler<NoopCommand> inner, int maxAttempts = 3) =>
        new(new MetadataAwareCommandHandler<NoopCommand>(new UnitOfWorkAwareCommandHandler<NoopCommand>(inner, _ => ValueTask.CompletedTask)), maxAttempts);
}
