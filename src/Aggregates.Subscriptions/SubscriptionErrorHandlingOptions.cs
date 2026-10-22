namespace Aggregates.Subscriptions;

/// <summary>
/// Controls how a subscription retries failed events before parking them.
/// </summary>
public sealed class SubscriptionErrorHandlingOptions {
    /// <summary>
    /// Maximum number of attempts per message (including the first attempt).
    /// After this many failures the message is parked. Default: 5. At least 0.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Delay before the second attempt. Subsequent attempts use exponential backoff capped at
    /// <see cref="MaxDelay"/>. Default: 1 second. Not negative.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound on the delay between retries. Default: 30 seconds. At least <see cref="InitialDelay"/>.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier applied to the delay after each failed attempt. Default: 2.0 (doubles each time). At least 1.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Checks the options, and returns them.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    internal SubscriptionErrorHandlingOptions Validate() {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetries, nameof(MaxRetries));
        ArgumentOutOfRangeException.ThrowIfLessThan(InitialDelay, TimeSpan.Zero, nameof(InitialDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDelay, InitialDelay, nameof(MaxDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(BackoffMultiplier, 1.0, nameof(BackoffMultiplier));
        return this;
    }
}
