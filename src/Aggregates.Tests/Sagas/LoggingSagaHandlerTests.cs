using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Sagas;

public class LoggingSagaHandlerTests {
    readonly ISagaRepository<TestSagaState, TestEvent> _repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
    readonly ISaga<TestSagaState, TestEvent> _saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
    readonly FakeLogger<LoggingSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>> _logger = new();

    public LoggingSagaHandlerTests() {
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
    }

    [Fact]
    public async Task GivenSuccess_LogsHandlingAndHandled() {
        await BuildHandler().HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handling"))
            .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handled"));
    }

    [Fact]
    public async Task LogsTheEventTypeTheSagaAndTheSagaId() {
        await BuildHandler().HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Message == "Handling TestEvent in ISaga`2 for saga saga-1");
    }

    [Fact]
    public async Task GivenException_LogsErrorAndRethrows() {
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();

        var act = () => BuildHandler().HandleAsync("saga-1", new TestEvent(1), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Error);
    }

    // The interface itself as TSaga, so the saga can be faked behind the real chain.
    LoggingSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildHandler() {
        var inner = new SagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(_repository, _saga, A.Fake<ICommandDispatcher>());
        var uow = new UnitOfWorkAwareSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(inner, _ => ValueTask.CompletedTask);
        var retry = new RetrySagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(uow);
        return new(retry, _logger);
    }
}
