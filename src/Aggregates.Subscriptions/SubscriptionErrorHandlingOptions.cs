namespace Aggregates.Subscriptions;

/// <summary>
/// Controls how a subscription retries failed events before parking them.
/// </summary>
public sealed class SubscriptionErrorHandlingOptions {
    /// <summary>
    /// Maximum number of attempts per message (including the first attempt).
    /// After this many failures the message is parked. Default: 5.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Delay before the second attempt. Subsequent attempts use exponential backoff capped at
    /// <see cref="MaxDelay"/>. Default: 1 second.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound on the delay between retries. Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier applied to the delay after each failed attempt. Default: 2.0 (doubles each time).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;
}
