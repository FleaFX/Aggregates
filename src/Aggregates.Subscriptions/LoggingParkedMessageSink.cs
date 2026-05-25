using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// A no-store <see cref="IParkedMessageSink"/> that logs a warning when a message is parked.
/// Used as a fallback when no transport-specific sink has been registered; messages are not
/// persisted and cannot be replayed.
/// </summary>
public sealed class LoggingParkedMessageSink(ILogger<LoggingParkedMessageSink> logger) : IParkedMessageSink {
    /// <inheritdoc/>
    public ValueTask ParkAsync(
        string subscriptionId,
        SubscriptionMessage message,
        Exception exception,
        CancellationToken cancellationToken = default) {
        logger.LogWarning(
            exception,
            "Message at position {CommitPosition} could not be processed by subscription '{SubscriptionId}' " +
            "after exhausting all retries. No parked-message store is configured — the message will not be persisted.",
            message.CommitPosition,
            subscriptionId);
        return ValueTask.CompletedTask;
    }
}
