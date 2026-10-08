using Microsoft.Extensions.Logging;

namespace Aggregates.Sagas;

/// <summary>
/// Decorates a <see cref="RetrySagaHandler{TSaga,TSagaState,TEvent}"/> with structured logging.
/// Logs at <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Error"/> when the inner handler throws.
/// </summary>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type the saga reacts to.</typeparam>
sealed partial class LoggingSagaHandler<TSaga, TSagaState, TEvent>(
    RetrySagaHandler<TSaga, TSagaState, TEvent> inner,
    ILogger<LoggingSagaHandler<TSaga, TSagaState, TEvent>> logger)
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <summary>
    /// Handles <paramref name="event"/> for <paramref name="sagaId"/> through the inner handler,
    /// logging the outcome.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance to update.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default) {
        var eventType = @event?.GetType().Name ?? typeof(TEvent).Name;
        var saga = typeof(TSaga).Name;
        LogHandling(logger, eventType, saga, sagaId);
        try {
            await inner.HandleAsync(sagaId, @event, cancellationToken);
            LogHandled(logger, eventType, saga, sagaId);
        } catch (Exception ex) {
            LogFailed(logger, ex, eventType, saga, sagaId);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {EventType} in {Saga} for saga {SagaId}")]
    static partial void LogHandling(ILogger logger, string eventType, string saga, AggregateIdentifier sagaId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {EventType} in {Saga} for saga {SagaId}")]
    static partial void LogHandled(ILogger logger, string eventType, string saga, AggregateIdentifier sagaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle {EventType} in {Saga} for saga {SagaId}")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType, string saga, AggregateIdentifier sagaId);
}
