using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Sagas;

public class LoggingSagaHandlerTests {
    [Fact]
    public async Task GivenSuccess_LogsHandlingAndHandled() {
        var logger = new FakeLogger<LoggingSagaHandler<TestSagaState, TestEvent>>();
        var handler = BuildHandler(A.Fake<SagaHandler<TestSagaState, TestEvent>>(), logger);

        await handler.HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handling"))
            .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handled"));
    }

    [Fact]
    public async Task GivenException_LogsErrorAndRethrows() {
        var logger = new FakeLogger<LoggingSagaHandler<TestSagaState, TestEvent>>();
        var inner = A.Fake<SagaHandler<TestSagaState, TestEvent>>();
        A.CallTo(() => inner.HandleAsync(A<AggregateIdentifier>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        var handler = BuildHandler(inner, logger);

        var act = () => handler.HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Error);
    }

    static LoggingSagaHandler<TestSagaState, TestEvent> BuildHandler(
        SagaHandler<TestSagaState, TestEvent> inner,
        FakeLogger<LoggingSagaHandler<TestSagaState, TestEvent>> logger) {
        var uow = new UnitOfWorkAwareSagaHandler<TestSagaState, TestEvent>(inner, _ => ValueTask.CompletedTask);
        var retry = new RetrySagaHandler<TestSagaState, TestEvent>(uow);
        return new LoggingSagaHandler<TestSagaState, TestEvent>(retry, logger);
    }
}
