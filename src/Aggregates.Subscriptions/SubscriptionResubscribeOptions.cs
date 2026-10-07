namespace Aggregates.Subscriptions;

/// <summary>
/// Controls how a subscription waits before it subscribes again after the subscription itself
/// failed (connection loss, server restart, checkpoint store unavailable). There is no maximum
/// number of attempts: the subscription keeps trying until the host stops.
/// </summary>
public sealed class SubscriptionResubscribeOptions {
    /// <summary>
    /// Delay before the first new attempt. Subsequent attempts use exponential backoff capped at
    /// <see cref="MaxDelay"/>. The backoff starts over once a message has been processed. Default: 1 second.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound on the delay between attempts. Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier applied to the delay after each failed attempt. Default: 2.0 (doubles each time).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;
}
