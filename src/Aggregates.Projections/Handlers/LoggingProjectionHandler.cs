using Microsoft.Extensions.Logging;

namespace Aggregates.Projections;

/// <summary>
/// Decorates a <see cref="ProjectionHandler{TEvent}"/> with structured logging. Logs at
/// <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Error"/> when the inner handler throws.
/// </summary>
/// <typeparam name="TEvent">The event type this handler processes.</typeparam>
sealed partial class LoggingProjectionHandler<TEvent>(ProjectionHandler<TEvent> inner, ILogger<LoggingProjectionHandler<TEvent>> logger)
    : IProjectionHandler<TEvent> {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default) {
        var eventType = typeof(TEvent).Name;
        LogHandling(logger, eventType);
        try {
            await inner.HandleAsync(@event, cancellationToken);
            LogHandled(logger, eventType);
        } catch (Exception ex) {
            LogFailed(logger, ex, eventType);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Projecting {EventType}")]
    static partial void LogHandling(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Projected {EventType}")]
    static partial void LogHandled(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to project {EventType}")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType);
}
