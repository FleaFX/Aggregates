using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// The log messages of the scheduler: <see cref="Scheduler"/> and the types of its runs log them
/// under the category of the scheduler.
/// </summary>
static partial class SchedulerLog {
    /// <summary>
    /// The scheduler sleeps until its handler defers its first command.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduler '{SubscriptionId}' waits until its handler defers its first command")]
    public static partial void LogWaiting(this ILogger logger, string subscriptionId);

    /// <summary>
    /// The handler deferred its first command: the scheduler starts.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler '{SubscriptionId}' starts: its handler deferred its first command")]
    public static partial void LogActivated(this ILogger logger, string subscriptionId);

    /// <summary>
    /// The scheduler adopted the checkpoint of its nearest predecessor.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler '{SubscriptionId}' has no checkpoint; continuing from '{Predecessor}' after position {Position}")]
    public static partial void LogContinuingFromPredecessor(this ILogger logger, string subscriptionId, string predecessor, ulong position);

    /// <summary>
    /// The scheduler adopted the checkpoint of an older predecessor, because the nearer ones have none.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler '{SubscriptionId}' has no checkpoint; continuing from '{Predecessor}' after position {Position}, because {Skipped} has no checkpoint")]
    public static partial void LogContinuingFromOlderPredecessor(this ILogger logger, string subscriptionId, string predecessor, ulong position, string skipped);

    /// <summary>
    /// The scheduler starts reading.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler '{SubscriptionId}' reading after position {Position}")]
    public static partial void LogReading(this ILogger logger, string subscriptionId, ulong? position);

    /// <summary>
    /// The scheduler read back its marker: it knows every stored request.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler '{SubscriptionId}' caught up with {Count} open request(s); the next one is due at {Due}")]
    public static partial void LogCaughtUp(this ILogger logger, string subscriptionId, int count, DateTimeOffset? due);

    /// <summary>
    /// The scheduler read a request of its handler.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduler '{SubscriptionId}' read request {Request} in {Stream}, due at {Due}: {CommandType}")]
    public static partial void LogRequested(this ILogger logger, string subscriptionId, EventReference request, string stream, DateTimeOffset due, string commandType);

    /// <summary>
    /// The command of a request can't be deserialized.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler '{SubscriptionId}' can't deserialize the command of request {Request} in {Stream}, due at {Due}; it is parked when it is due, unless a deploy fixes it before")]
    public static partial void LogUndeserializable(this ILogger logger, Exception exception, string subscriptionId, EventReference request, string stream, DateTimeOffset due);

    /// <summary>
    /// A request whose data can't be read is skipped.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler '{SubscriptionId}' skips the invalid request {Request}")]
    public static partial void LogInvalidRequest(this ILogger logger, string subscriptionId, EventReference request);

    /// <summary>
    /// An action failed transiently and is tried again.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler '{SubscriptionId}' could not {Action}; trying again in {Delay} (attempt {Attempt})")]
    public static partial void LogRetrying(this ILogger logger, Exception exception, string subscriptionId, string action, TimeSpan delay, int attempt);

    /// <summary>
    /// The feed failed transiently and subscribes again.
    /// </summary>
    [LoggerMessage(Message = "Scheduler '{SubscriptionId}' failed at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    public static partial void LogFeedFailed(this ILogger logger, LogLevel level, Exception exception, string subscriptionId, ulong? position, TimeSpan delay, int attempt);

    /// <summary>
    /// The feed ended without a failure and subscribes again.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler '{SubscriptionId}' ended unexpectedly at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    public static partial void LogFeedEnded(this ILogger logger, string subscriptionId, ulong? position, TimeSpan delay, int attempt);

    /// <summary>
    /// A due request could not be delivered and is tried again.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler '{SubscriptionId}' could not deliver request {Request}; trying again in {Delay} (attempt {Attempt})")]
    public static partial void LogDeliveryFailed(this ILogger logger, Exception exception, string subscriptionId, EventReference request, TimeSpan delay, int attempt);

    /// <summary>
    /// The scheduler stopped after a failure that is not transient.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Critical, Message = "Scheduler '{SubscriptionId}' stopped after a failure that is not transient")]
    public static partial void LogStopped(this ILogger logger, Exception exception, string subscriptionId);

    /// <summary>
    /// The final checkpoint could not be written.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduler '{SubscriptionId}' could not write its final checkpoint {Position}")]
    public static partial void LogFinalFlushFailed(this ILogger logger, Exception exception, string subscriptionId, ulong? position);
}
