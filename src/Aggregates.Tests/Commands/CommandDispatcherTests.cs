using FakeItEasy;
using FluentAssertions;

namespace Aggregates;

public class CommandDispatcherTests {
    [Fact]
    public async Task DispatchAsync_ResolvesHandlerByConcreteTypeAndCallsHandleAsync() {
        var handler = A.Fake<ICommandHandler<NoopCommand>>();
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ICommandHandler<NoopCommand>)))
            .Returns(handler);
        var dispatcher = new CommandDispatcher(serviceProvider);
        var command = new NoopCommand();

        await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        A.CallTo(() => handler.HandleAsync(command, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DispatchAsync_WhenCommandPassedAsInterface_StillResolvesConcreteHandler() {
        // Verifies that passing a command as ICommand does not lose the concrete type.
        var handler = A.Fake<ICommandHandler<NoopCommand>>();
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ICommandHandler<NoopCommand>)))
            .Returns(handler);
        var dispatcher = new CommandDispatcher(serviceProvider);
        ICommand command = new NoopCommand();   // typed as ICommand at the call site

        await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        A.CallTo(() => handler.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerNotRegistered_ThrowsInvalidOperationException() {
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ICommandHandler<NoopCommand>)))
            .Returns(null);
        var dispatcher = new CommandDispatcher(serviceProvider);

        var act = () => dispatcher.DispatchAsync(new NoopCommand(), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
