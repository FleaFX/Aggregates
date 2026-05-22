using Microsoft.Extensions.Logging;

namespace Aggregates.Sagas;

/// <summary>
/// Decorates a <see cref="RetrySagaHandler{TSagaState,TEvent}"/> with structured logging. Logs at
/// <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Error"/> when the inner handler throws.
/// </summary>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type this handler processes.</typeparam>
sealed partial class LoggingSagaHandler<TSagaState, TEvent>(RetrySagaHandler<TSagaState, TEvent> inner, ILogger<LoggingSagaHandler<TSagaState, TEvent>> logger)
    : ISagaHandler<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default) {
        var eventType = typeof(TEvent).Name;
        LogHandling(logger, eventType, sagaId);
        try {
            await inner.HandleAsync(sagaId, @event, cancellationToken);
            LogHandled(logger, eventType, sagaId);
        } catch (Exception ex) {
            LogFailed(logger, ex, eventType, sagaId);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {EventType} for saga {SagaId}")]
    static partial void LogHandling(ILogger logger, string eventType, AggregateIdentifier sagaId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {EventType} for saga {SagaId}")]
    static partial void LogHandled(ILogger logger, string eventType, AggregateIdentifier sagaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle {EventType} for saga {SagaId}")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType, AggregateIdentifier sagaId);
}
