using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Sagas;

public class LoggingSagaHandlerTests {
    readonly ISagaRepository<TestSagaState, TestEvent> _repository = A.Fake<ISagaRepository<TestSagaState, TestEvent>>();
    readonly ISaga<TestSagaState, TestEvent, TestDeadline> _saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
    readonly FakeLogger<LoggingSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>> _logger = new();

    public LoggingSagaHandlerTests() {
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(null));
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        A.CallTo(() => _saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
    }

    [Fact]
    public async Task GivenSuccess_LogsHandlingAndHandled() {
        await BuildHandler().HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handling"))
            .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handled"));
    }

    [Fact]
    public async Task LogsTheEventTypeTheSourceTheSagaAndTheSagaId() {
        await BuildHandler().HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Message == "Handling TestEvent (0@stream-1) in ISaga`2 for saga saga-1");
    }

    [Fact]
    public async Task GivenException_LogsErrorAndRethrows() {
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();

        var act = () => BuildHandler().HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_WhenCancelled_LogsNoWarning() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        A.CallTo(() => _saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException(cts.Token));

        var act = () => BuildHandler().HandleAsync("saga-1", new TestEvent(1), new EventReference("stream-1", 0), cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        _logger.Collector.GetSnapshot()
            .Should().NotContain(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task HandleDeadlineAsync_LogsTheDeadlineTypeTheRequestTheSagaAndTheSagaId() {
        A.CallTo(() => _repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<SagaRoot<TestSagaState, TestEvent>?>(new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(4), AggregateVersion.None)));

        await BuildHandler().HandleDeadlineAsync("saga-1", new TestDeadline(2), new EventReference("saga-1", 2), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Message == "Handling deadline TestDeadline (2@saga-1) in ISaga`2 for saga saga-1");
    }

    [Fact]
    public async Task HandleDeadlineAsync_WhenTheSagaDoesNotExist_LogsAWarning() {
        await BuildHandler().HandleDeadlineAsync("saga-1", new TestDeadline(2), new EventReference("saga-1", 2), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Warning && r.Message.Contains("doesn't exist anymore"));
    }

    // The interface itself as TSaga, so the saga can be faked behind the real chain.
    LoggingSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent> BuildHandler() {
        var inner = new SagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(_repository, _saga, A.Fake<ICommandDispatcher>(), new SchedulerActivation<ISaga<TestSagaState, TestEvent>>(A.Fake<IScheduledStore>(), []));
        var uow = new UnitOfWorkAwareSagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(inner, (_, _) => ValueTask.CompletedTask);
        var retry = new RetrySagaHandler<ISaga<TestSagaState, TestEvent>, TestSagaState, TestEvent>(uow);
        return new(retry, _logger);
    }
}
