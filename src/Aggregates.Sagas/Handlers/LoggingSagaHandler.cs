using Aggregates.Subscriptions;
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
    /// <param name="source">Identifies the stored event that triggered the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, EventReference source, CancellationToken cancellationToken = default) {
        var eventType = @event?.GetType().Name ?? typeof(TEvent).Name;
        var saga = typeof(TSaga).Name;
        LogHandling(logger, eventType, source, saga, sagaId);
        try {
            await inner.HandleAsync(sagaId, @event, source, cancellationToken);
            LogHandled(logger, eventType, source, saga, sagaId);
        } catch (Exception ex) {
            LogFailed(logger, ex, eventType, source, saga, sagaId);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {EventType} ({Source}) in {Saga} for saga {SagaId}")]
    static partial void LogHandling(ILogger logger, string eventType, EventReference source, string saga, AggregateIdentifier sagaId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {EventType} ({Source}) in {Saga} for saga {SagaId}")]
    static partial void LogHandled(ILogger logger, string eventType, EventReference source, string saga, AggregateIdentifier sagaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle {EventType} ({Source}) in {Saga} for saga {SagaId}")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType, EventReference source, string saga, AggregateIdentifier sagaId);
}
