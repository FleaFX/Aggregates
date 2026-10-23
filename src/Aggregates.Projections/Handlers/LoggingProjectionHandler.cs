using Microsoft.Extensions.Logging;

namespace Aggregates.Projections;

/// <summary>
/// Decorates a <see cref="ProjectionHandler{TProjection,TEvent}"/> with structured logging. Logs at
/// <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Warning"/> when the inner handler throws (the retry policy logs the message
/// it parks); a cancellation by the caller is not logged.
/// </summary>
/// <typeparam name="TProjection">The projection class.</typeparam>
/// <typeparam name="TEvent">The event type the projection handles.</typeparam>
sealed partial class LoggingProjectionHandler<TProjection, TEvent>(
    ProjectionHandler<TProjection, TEvent> inner,
    ILogger<LoggingProjectionHandler<TProjection, TEvent>> logger)
    where TProjection : IProjection<TEvent> {

    /// <summary>
    /// Projects <paramref name="event"/> through the inner handler, logging the outcome.
    /// </summary>
    /// <param name="event">The event to project.</param>
    /// <param name="metadata">The metadata stored alongside <paramref name="event"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
        var eventType = @event?.GetType().Name ?? typeof(TEvent).Name;
        var projection = typeof(TProjection).Name;
        LogHandling(logger, eventType, projection);
        try {
            await inner.HandleAsync(@event, metadata, cancellationToken);
            LogHandled(logger, eventType, projection);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            LogFailed(logger, ex, eventType, projection);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Projecting {EventType} in {Projection}")]
    static partial void LogHandling(ILogger logger, string eventType, string projection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Projected {EventType} in {Projection}")]
    static partial void LogHandled(ILogger logger, string eventType, string projection);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to project {EventType} in {Projection}; the subscription retries or parks it")]
    static partial void LogFailed(ILogger logger, Exception exception, string eventType, string projection);
}
