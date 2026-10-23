using FakeItEasy;
using FluentAssertions;

namespace Aggregates;

public class UnitOfWorkAwareCommandHandlerTests {
    [Fact]
    public async Task GivenInnerHandlerSucceeds_InvokesCommitDelegate() {
        var committed = false;
        var handler = new UnitOfWorkAwareCommandHandler<NoopCommand>(
            A.Fake<CommandHandler<NoopCommand>>(),
            (_, _) => { committed = true; return ValueTask.CompletedTask; });

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        committed.Should().BeTrue();
    }

    [Fact]
    public async Task GivenInnerHandlerThrows_DoesNotInvokeCommitDelegate() {
        var committed = false;
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        var handler = new UnitOfWorkAwareCommandHandler<NoopCommand>(
            inner,
            (_, _) => { committed = true; return ValueTask.CompletedTask; });

        var act = () => handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        committed.Should().BeFalse();
    }

    [Fact]
    public async Task GivenInnerHandler_UnitOfWorkIsAvailableViaAmbientScope() {
        UnitOfWork? captured = null;
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => captured = UnitOfWorkScope.Current?.UnitOfWork)
            .Returns(ValueTask.CompletedTask);
        var handler = new UnitOfWorkAwareCommandHandler<NoopCommand>(inner, (_, _) => ValueTask.CompletedTask);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenMultipleCalls_EachCallGetsFreshUnitOfWork() {
        var unitOfWorks = new List<UnitOfWork?>();
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => unitOfWorks.Add(UnitOfWorkScope.Current?.UnitOfWork))
            .Returns(ValueTask.CompletedTask);
        var handler = new UnitOfWorkAwareCommandHandler<NoopCommand>(inner, (_, _) => ValueTask.CompletedTask);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);
        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        unitOfWorks.Should().HaveCount(2);
        unitOfWorks[0].Should().NotBeSameAs(unitOfWorks[1]);
    }

    [Fact]
    public async Task PassesTheCommandsCancellationTokenToTheCommitDelegate() {
        using var cts = new CancellationTokenSource();
        CancellationToken? captured = null;
        var handler = new UnitOfWorkAwareCommandHandler<NoopCommand>(
            A.Fake<CommandHandler<NoopCommand>>(),
            (_, cancellationToken) => { captured = cancellationToken; return ValueTask.CompletedTask; });

        await handler.HandleAsync(new NoopCommand(), cts.Token);

        captured.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GivenTheTokenIsCancelledDuringTheCommit_StopsWaiting() {
        using var cts = new CancellationTokenSource();
        var commitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new UnitOfWorkAwareCommandHandler<NoopCommand>(
            A.Fake<CommandHandler<NoopCommand>>(),
            async (_, cancellationToken) => {
                commitStarted.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            });

        var handling = handler.HandleAsync(new NoopCommand(), cts.Token).AsTask();
        await commitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var act = () => handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
