using Microsoft.Extensions.Logging;

namespace Aggregates.Policies;

/// <summary>
/// Decorates a <see cref="PolicyHandler{TPolicy,TEvent}"/> with structured logging. Logs at
/// <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Error"/> when the inner handler throws.
/// </summary>
/// <typeparam name="TPolicy">The policy class.</typeparam>
/// <typeparam name="TEvent">The event type the policy reacts to.</typeparam>
sealed partial class LoggingPolicyHandler<TPolicy, TEvent>(
    PolicyHandler<TPolicy, TEvent> inner,
    ILogger<LoggingPolicyHandler<TPolicy, TEvent>> logger)
    where TPolicy : IPolicy<TEvent> {

    /// <summary>
    /// Handles <paramref name="event"/> through the inner handler, logging the outcome.
    /// </summary>
    /// <param name="event">The event to react to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default) {
        var eventType = @event?.GetType().Name ?? typeof(TEvent).Name;
        var policy = typeof(TPolicy).Name;
        LogHandling(logger, eventType, policy);
        try {
            await inner.HandleAsync(@event, cancellationToken);
            LogHandled(logger, eventType, policy);
        } catch (Exception ex) {
            LogFailed(logger, ex, eventType, policy);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {EventType} in {Policy}")]
    static partial void LogHandling(ILogger logger, string eventType, string policy);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {EventType} in {Policy}")]
    static partial void LogHandled(ILogger logger, string eventType, string policy);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle {EventType} in {Policy}")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType, string policy);
}
