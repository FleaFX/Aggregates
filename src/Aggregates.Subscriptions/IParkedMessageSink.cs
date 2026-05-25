namespace Aggregates.Subscriptions;

/// <summary>
/// Receives messages that could not be processed after all retry attempts have been exhausted.
/// Implementations are responsible for storing the message for later inspection or replay.
/// </summary>
public interface IParkedMessageSink {
    /// <summary>
    /// Parks <paramref name="message"/> that failed processing under <paramref name="subscriptionId"/>.
    /// </summary>
    /// <param name="subscriptionId">The subscription that owns this message.</param>
    /// <param name="message">The message that could not be processed.</param>
    /// <param name="exception">The exception that caused the final failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask ParkAsync(string subscriptionId, SubscriptionMessage message, Exception exception, CancellationToken cancellationToken = default);
}
