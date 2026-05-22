using Microsoft.Extensions.Logging;

namespace Aggregates.Policies;

/// <summary>
/// Decorates a <see cref="PolicyHandler{TEvent}"/> with structured logging. Logs at
/// <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Error"/> when the inner handler throws.
/// </summary>
/// <typeparam name="TEvent">The event type this handler processes.</typeparam>
sealed partial class LoggingPolicyHandler<TEvent>(PolicyHandler<TEvent> inner, ILogger<LoggingPolicyHandler<TEvent>> logger)
    : IPolicyHandler<TEvent> {

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {EventType}")]
    static partial void LogHandling(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {EventType}")]
    static partial void LogHandled(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle {EventType}")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType);
}
