using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task DispatchAsync_GivenADeferredCommand_ExplainsThatOnlySagasAndPoliciesDeferCommands() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(provider);

        var act = () => dispatcher.DispatchAsync(new DeferredCommand(due: DateTimeOffset.UtcNow, command: new NoopCommand()), TestContext.Current.CancellationToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("A DeferredCommand is executed by the scheduler of a saga or policy");
    }

    [Fact]
    public async Task DispatchAsync_GivenAnUnscannedCommand_NamesTheCommandAndScanAssemblies() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(provider);

        var act = () => dispatcher.DispatchAsync(new NoopCommand(), TestContext.Current.CancellationToken).AsTask();

        var message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        message.Should().Contain(nameof(NoopCommand));
        message.Should().Contain("ScanAssemblies").And.Contain("ScanTypes");
    }

    [Fact]
    public async Task DispatchAsync_GivenAScannedCommandWithoutCommitDelegate_KeepsTheOriginalError() {
        var services = new ServiceCollection();
        services.AddAggregates(o => o.ScanTypes(typeof(ScanTestCommand)));
        using var provider = services.BuildServiceProvider();
        var dispatcher = new CommandDispatcher(provider);

        var act = () => dispatcher.DispatchAsync(new ScanTestCommand("agg/1"), TestContext.Current.CancellationToken).AsTask();

        var message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        message.Should().Contain("Unable to resolve");
        message.Should().NotContain("No handler for");
    }
}
