using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates;

public class LoggingCommandHandlerTests {
    [Fact]
    public async Task GivenSuccess_LogsHandlingAndHandled() {
        var logger = new FakeLogger<LoggingCommandHandler<NoopCommand>>();
        var handler = BuildHandler(A.Fake<CommandHandler<NoopCommand>>(), logger);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handling"))
            .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handled"));
    }

    [Fact]
    public async Task GivenException_LogsErrorAndRethrows() {
        var logger = new FakeLogger<LoggingCommandHandler<NoopCommand>>();
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        var handler = BuildHandler(inner, logger);

        var act = () => handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Error);
    }

    static LoggingCommandHandler<NoopCommand> BuildHandler(
        CommandHandler<NoopCommand> inner,
        FakeLogger<LoggingCommandHandler<NoopCommand>> logger) {
        var uow = new UnitOfWorkAwareCommandHandler<NoopCommand>(inner, _ => ValueTask.CompletedTask);
        var metadata = new MetadataAwareCommandHandler<NoopCommand>(uow);
        var retry = new RetryCommandHandler<NoopCommand>(metadata);
        return new LoggingCommandHandler<NoopCommand>(retry, logger);
    }
}
