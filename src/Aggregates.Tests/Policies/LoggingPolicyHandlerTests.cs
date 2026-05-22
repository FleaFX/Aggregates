using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Policies;

public class LoggingPolicyHandlerTests {
    [Fact]
    public async Task GivenSuccess_LogsHandlingAndHandled() {
        var logger = new FakeLogger<LoggingPolicyHandler<PolicyTestEvent>>();
        var handler = BuildHandler(A.Fake<PolicyHandler<PolicyTestEvent>>(), logger);

        await handler.HandleAsync(new PolicyTestEvent(1), TestContext.Current.CancellationToken);

        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handling"))
            .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handled"));
    }

    [Fact]
    public async Task GivenException_LogsErrorAndRethrows() {
        var logger = new FakeLogger<LoggingPolicyHandler<PolicyTestEvent>>();
        var inner = A.Fake<PolicyHandler<PolicyTestEvent>>();
        A.CallTo(() => inner.HandleAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        var handler = BuildHandler(inner, logger);

        var act = () => handler.HandleAsync(new PolicyTestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Error);
    }

    static LoggingPolicyHandler<PolicyTestEvent> BuildHandler(
        PolicyHandler<PolicyTestEvent> inner,
        FakeLogger<LoggingPolicyHandler<PolicyTestEvent>> logger) =>
        new(inner, logger);
}
