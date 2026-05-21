using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class CommandDispatcherTests {
    [Fact]
    public async Task DispatchAsync_ResolvesHandlerAndCallsHandleAsync() {
        var handler = A.Fake<ICommandHandler<TestCommand>>();
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ICommandHandler<TestCommand>)))
            .Returns(handler);
        var dispatcher = new CommandDispatcher(serviceProvider);
        var command = new TestCommand();

        await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        A.CallTo(() => handler.HandleAsync(command, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerNotRegistered_ThrowsInvalidOperationException() {
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(ICommandHandler<TestCommand>)))
            .Returns(null);
        var dispatcher = new CommandDispatcher(serviceProvider);

        var act = () => dispatcher.DispatchAsync(new TestCommand(), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
